using System.Drawing;

namespace PoeAncientsPriceHelper;

// Background auto-detect loop for the Currency Exchange helper. Gated cheaply by an OCR check over
// the viewport's upper-centre band, where both exchange views put their identity text (the floating
// window's "CURRENCY EXCHANGE" title, a picker's big "I WANT"/"I HAVE" header). Off the exchange the
// loop only does that band check (~0.8 Hz); on it, full-frame detection runs on a throttle:
//   main view  → refresh the remembered pair + show the ninja-ratio pill under "Market Ratio";
//   picker     → badges against the OPPOSITE side's remembered currency, iff it is known.
// ESC / Left-Ctrl+click force-dismiss via a latch that re-arms once that view is gone.
internal sealed class ExchangeScanEngine : IDisposable
{
    private readonly IScreenCaptureBackend _capture;
    private readonly OcrScanner _ocr;
    private readonly Func<PriceRepository?> _prices;
    private readonly Func<Rectangle> _screen;
    private readonly Func<bool> _enabled;
    private readonly Func<int> _intervalMs;
    private readonly Func<string> _manualBaseKey;
    private readonly Func<bool> _pauseWhenNotFocused;
    private readonly NameTranslator _translator;
    private readonly ExchangePairState _pair = new();
    // Serialises capture+OCR against the shared backend (mirrors RumourScanner's gate).
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Rebuilt whenever the price snapshot generation changes, so cell resolution always runs against
    // the live exchange view (league changes swap the repository instance — hence the accessor).
    private ExchangeNameResolver? _resolver;
    private int _resolverGeneration = -1;
    private PriceRepository? _resolverRepo;

    private static volatile bool _dismissed;
    private static volatile bool _showing;
    public static bool IsShowing
    {
        get => _showing;
        private set
        {
            if (_showing == value) return;
            _showing = value;
            App.UpdateClickWatcher();
        }
    }
    public static void RequestDismiss() => _dismissed = true;

    private const int TickMs = 150;
    private const int GateIntervalMs = 1200;
    private const int HideAfterMisses = 2;      // consecutive None passes before the overlay hides
    private const int MinIntervalMs = 300;
    private const int MaxIntervalMs = 5000;
    // A detection hit keeps the loop in "on exchange" mode this long without needing the gate — the
    // gate is only the ENTRY signal; once detected, detection itself sustains the scanning.
    private const int DetectionSustainMs = 4000;

    public ExchangeScanEngine(IScreenCaptureBackend capture, OcrScanner ocr, Func<PriceRepository?> prices,
        Func<Rectangle> screen, Func<bool> enabled, Func<int> intervalMs, Func<string> manualBaseKey,
        Func<bool> pauseWhenNotFocused, string gameLanguage)
    {
        _capture = capture;
        _ocr = ocr;
        _prices = prices;
        _screen = screen;
        _enabled = enabled;
        _intervalMs = intervalMs;
        _manualBaseKey = manualBaseKey;
        _pauseWhenNotFocused = pauseWhenNotFocused;
        _translator = NameTranslator.ForLanguage(gameLanguage);
    }

    public static int ClampInterval(int ms) => Math.Clamp(ms, MinIntervalMs, MaxIntervalMs);

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _dismissed = false;
        IsShowing = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void StopAndWait(TimeSpan timeout)
    {
        _cts?.Cancel();
        try { _loop?.Wait(timeout); } catch { }
        HideOverlay();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        bool onExchange = false;
        int missStreak = 0;
        var lastGate = DateTime.MinValue;
        var lastScan = DateTime.MinValue;
        var lastHit = DateTime.MinValue;
        string dismissedViewSig = "";
        int dismissGoneStreak = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_enabled())
                {
                    HideOverlay();
                    onExchange = false;
                    missStreak = 0;
                    _pair.Reset();
                    try { await Task.Delay(TickMs, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }

                var now = DateTime.UtcNow;
                var screen = _screen();

                // Pause while the game isn't in front — unless the user disabled the gate (#49
                // semantics, same as the price loop). Fail-open when the window can't be located.
                bool poeFound = GameWindow.TryGet(out var game);
                if (poeFound && !game.IsForeground && _pauseWhenNotFocused())
                {
                    HideOverlay();
                    onExchange = false;
                    missStreak = 0;
                    try { await Task.Delay(TickMs, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }
                var area = poeFound ? game.ClientBounds : screen;

                // Cheap identity gate. A recent detection hit sustains scanning by itself so a gate
                // misread mid-session can't blink the overlay off.
                if ((now - lastHit).TotalMilliseconds < DetectionSustainMs)
                {
                    onExchange = true;
                }
                else if ((now - lastGate).TotalMilliseconds >= GateIntervalMs)
                {
                    lastGate = now;
                    var band = GateRegion(area);
                    var gateLines = CaptureLines(band, GateUpscale(band.Height));
                    bool nowOnExchange = gateLines.Any(l => ExchangeScreenDetector.IsAnySignature(l.Text));
                    if (nowOnExchange && !onExchange)
                        ExchangeDiag.Log($"gate HIT band={band} lines={gateLines.Count}");
                    else if (!nowOnExchange)
                        ExchangeDiag.Quiet($"gate miss band={band} lines={gateLines.Count} " +
                            $"(no 'currency exchange' / 'i want' / 'i have' in the band)");
                    if (!nowOnExchange && onExchange)
                    {
                        // Left the exchange between scans (sustain expired, gate no longer sees it):
                        // drop the overlay and clear any dismiss latch, mirroring the rumour engine's
                        // map-exit transition. Without this, at scan intervals ≥2s the 2-miss hide
                        // path can't fire inside the sustain window and stale badges stay on screen.
                        HideOverlay();
                        _dismissed = false;
                        missStreak = 0;
                    }
                    onExchange = nowOnExchange;
                }

                if (onExchange && (now - lastScan).TotalMilliseconds >= ClampInterval(_intervalMs()))
                {
                    lastScan = now;
                    var repo = _prices();
                    var snapshot = repo?.Exchange;
                    if (repo is null || snapshot is null || snapshot.Items.Count == 0)
                    {
                        // No prices yet (startup / total outage): nothing to badge with.
                        ExchangeDiag.Quiet($"hidden: no price snapshot (repo={(repo is null ? "null" : "ok")} " +
                            $"items={snapshot?.Items.Count ?? 0}) — poe.ninja fetch not landed yet or failing");
                        HideOverlay();
                    }
                    else
                    {
                        EnsureResolver(repo, snapshot);
                        var lines = CaptureLines(area);
                        var det = ExchangeScreenDetector.Detect(lines, _resolver!.Resolve);
                        ExchangeDiag.Log($"scan area={area} ocrLines={lines.Count} -> " +
                            (det.Main is { } m ? $"MAIN want={m.WantKey ?? "-"} have={m.HaveKey ?? "-"}"
                             : det.Picker is { } p ? $"PICKER side={p.Side} cells={p.Cells.Count}"
                             : "NONE"));
                        if (det.IsNone && lines.Count > 0)
                            ExchangeDiag.Quiet($"detect NONE with {lines.Count} OCR lines — either no " +
                                "'i want'/'i have' header resolved, or fewer than 3 currency cells matched " +
                                "(text too small for OCR at this scale?)");

                        if (!det.IsNone) lastHit = now;

                        if (_dismissed)
                        {
                            HideOverlay();
                            var sig = ViewSignature(det);
                            if (dismissedViewSig.Length == 0) dismissedViewSig = sig;
                            // Re-arm once the dismissed view is gone or replaced for 2 passes.
                            if (sig != dismissedViewSig)
                            {
                                if (++dismissGoneStreak >= 2)
                                {
                                    _dismissed = false;
                                    dismissedViewSig = "";
                                    dismissGoneStreak = 0;
                                }
                            }
                            else dismissGoneStreak = 0;
                        }
                        else
                        {
                            dismissedViewSig = "";
                            dismissGoneStreak = 0;
                            if (det.Main is { } main)
                            {
                                missStreak = 0;
                                _pair.ApplyMainView(main.WantKey, main.HaveKey);
                                ShowMainPill(main, snapshot, repo);
                            }
                            else if (det.Picker is { } picker)
                            {
                                missStreak = 0;
                                ShowPickerBadges(picker, snapshot, repo);
                            }
                            else if (++missStreak >= HideAfterMisses)
                            {
                                HideOverlay();
                                missStreak = 0;
                                onExchange = false;   // force the gate to re-confirm entry
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ExchangeScanEngine] {ex.GetType().Name}: {ex.Message}");
                ExchangeDiag.Log($"EXCEPTION {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }

            try { await Task.Delay(TickMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        HideOverlay();
    }

    // Main view: poe.ninja's aggregate ratio for the SELECTED pair + the want side's volume + age,
    // under the game's own "Market Ratio" header. Only when both sides are known and priced.
    private void ShowMainPill(ExchangeMainView main, ExchangeSnapshot snapshot, PriceRepository repo)
    {
        // Render from the CONFIRMED pair state (refreshed by ApplyMainView just before this call),
        // not this frame's raw read: a single-frame OCR miss of one slot name must not blink the
        // pill off — ExchangePairState holds the last confirmed pair through exactly that window.
        // (During a 2-read change-confirm the pill briefly shows the still-confirmed previous pair.)
        if (_pair.WantKey is not { } wantKey || _pair.HaveKey is not { } haveKey ||
            main.MarketRatioBounds is not { } anchor ||
            !snapshot.Items.TryGetValue(wantKey, out var want) ||
            !snapshot.Items.TryGetValue(haveKey, out var have))
        {
            HideOverlay();
            return;
        }
        // The want currency plays "cell" so the direct max-volume pair rate applies when available.
        var ratio = ExchangeRates.RatioFor(want, have, haveKey, cellIsWant: true);
        if (ratio is null) { HideOverlay(); return; }
        var (ageText, level) = Staleness(repo);
        var volume = ExchangeRates.FormatVolume(want.VolumePrimaryValue, snapshot.PrimaryCurrency);
        string text = volume is null
            ? $"ninja {ratio} · {ageText}"
            : $"ninja {ratio} · {volume} · {ageText}";
        ExchangeOverlayManager.ShowMain(text, anchor, level);
        IsShowing = true;
    }

    // Picker: one pill per resolved cell, against the opposite side's currency (manual override
    // wins). No base known → nothing shows: that IS the owner's show/don't-show state machine.
    private void ShowPickerBadges(ExchangePickerView picker, ExchangeSnapshot snapshot, PriceRepository repo)
    {
        var manual = _manualBaseKey();
        var baseKey = manual.Length > 0 ? manual : _pair.OppositeOf(picker.Side);
        if (baseKey is null || !snapshot.Items.TryGetValue(baseKey, out var baseEntry))
        {
            // The single most confusing dead end for a new user: a picker with nothing to price
            // against renders nothing at all, and looks identical to a broken build.
            ExchangeDiag.Quiet(baseKey is null
                ? $"hidden: no ratio base for the {picker.Side} picker — the opposite side isn't " +
                  "remembered yet (open the main exchange view first, or set a manual base in Settings)"
                : $"hidden: ratio base '{baseKey}' is not in the poe.ninja snapshot");
            HideOverlay();
            return;
        }

        bool cellIsWant = picker.Side == ExchangeSide.Want;
        var badges = new List<ExchangeBadge>(picker.Cells.Count);
        foreach (var cell in picker.Cells)
        {
            // Per-cell trace: a single missing badge among many is invisible from outside, and
            // "why did THAT one not price?" was the first question the fix for W-6 raised.
            if (cell.Key == baseKey) { ExchangeDiag.Log($"  cell '{cell.Key}' skip: is the ratio base"); continue; }
            if (!snapshot.Items.TryGetValue(cell.Key, out var entry))
            {
                ExchangeDiag.Log($"  cell '{cell.Key}' skip: no snapshot entry (resolved from OCR but " +
                    "absent from every fetched poe.ninja category)");
                continue;
            }
            if (ExchangeRates.RatioFor(entry, baseEntry, baseKey, cellIsWant) is not { } ratio)
            {
                ExchangeDiag.Log($"  cell '{cell.Key}' skip: no ratio vs '{baseKey}' " +
                    $"(primary={entry.PrimaryValue} maxVolCur={entry.MaxVolumeCurrency ?? "-"} " +
                    $"maxVolRate={entry.MaxVolumeRate?.ToString() ?? "-"})");
                continue;
            }
            ExchangeDiag.Log($"  cell '{cell.Key}' -> {ratio}");
            badges.Add(new ExchangeBadge(ratio,
                ExchangeRates.FormatVolume(entry.VolumePrimaryValue, snapshot.PrimaryCurrency),
                cell.Bounds));
        }
        if (badges.Count == 0)
        {
            ExchangeDiag.Quiet($"hidden: {picker.Cells.Count} cells detected but 0 priceable against " +
                $"'{baseKey}' (no snapshot entry, or no ratio computable)");
            HideOverlay();
            return;
        }

        var (ageText, level) = Staleness(repo);
        ExchangeDiag.Log($"SHOW picker side={picker.Side} base='{baseKey}' badges={badges.Count} " +
            $"panel={picker.PanelBounds} age={ageText}");
        ExchangeDiag.ClearQuiet();
        ExchangeOverlayManager.ShowPicker(badges, picker.PanelBounds, ageText, level);
        IsShowing = true;
    }

    private static (string AgeText, StalenessLevel Level) Staleness(PriceRepository repo)
    {
        if (repo.ItemCount == 0 || repo.LastFetchedAt is not { } at)
            return ("no data", StalenessLevel.Failing);
        var age = DateTime.Now - at;
        string text = age.TotalMinutes < 1 ? "<1m" : $"{(int)age.TotalMinutes}m";
        var level = age.TotalMinutes > 60 ? StalenessLevel.Failing
                  : age.TotalMinutes > 30 ? StalenessLevel.Stale
                  : StalenessLevel.Fresh;
        return (text, level);
    }

    private void EnsureResolver(PriceRepository repo, ExchangeSnapshot snapshot)
    {
        if (_resolver is not null && _resolverGeneration == repo.PriceGeneration &&
            ReferenceEquals(_resolverRepo, repo)) return;
        _resolver = new ExchangeNameResolver(snapshot, _translator);
        _resolverGeneration = repo.PriceGeneration;
        _resolverRepo = repo;
    }

    // What the dismiss latch keys on: dismissing a picker keeps THAT side's picker hidden; switching
    // to the main view (or the other side's picker) is a different view and re-arms.
    private static string ViewSignature(ExchangeDetection det) =>
        det.Main is not null ? "main" : det.Picker is { } p ? $"picker:{p.Side}" : "none";

    // Both exchange views put their identity in the viewport's upper-centre: a picker's header at
    // ~2% height, the floating window's title at ~12% (reference screenshots). Middle half of the
    // width × top fifth of the height covers both across resolutions while staying ~10% of the frame.
    public static Rectangle GateRegion(Rectangle area) =>
        new(area.Left + area.Width / 4, area.Top,
            Math.Max(1, area.Width / 2), Math.Max(1, area.Height / 5));

    // The band's text is title-sized (~20-30px at 1080p); a 2× upscale reads it reliably (the WORLD
    // gate needed 4× only because its band is far smaller). Large bands (4K) are already readable.
    internal static int GateUpscale(int regionHeight) => regionHeight is > 0 and < 300 ? 2 : 1;

    // Capture a region and return OCR lines with bounds shifted into absolute screen coords.
    private IReadOnlyList<OcrTextLine> CaptureLines(Rectangle region, int upscale = 1)
    {
        lock (_gate)
        {
            using var bmp = _capture.CaptureRegion(region);
            return _ocr.RecognizeLines(bmp, upscale: upscale)
                .Select(l => l with { Bounds = new Rectangle(
                    l.Bounds.X + region.X, l.Bounds.Y + region.Y, l.Bounds.Width, l.Bounds.Height) })
                .ToList();
        }
    }

    private void HideOverlay()
    {
        if (_showing) { ExchangeOverlayManager.HideNow(); IsShowing = false; }
    }

    public void Dispose()
    {
        StopAndWait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }
}
