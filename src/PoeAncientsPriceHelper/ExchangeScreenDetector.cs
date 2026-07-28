using System.Drawing;

namespace PoeAncientsPriceHelper;

internal enum ExchangeSide { Want, Have }

// One resolved picker grid cell: the exchange price key and the OCR'd name-text bounds (absolute
// screen coords). Anchor is the single line the badge should sit beside — for a wrapped two-line
// name that is the LOWER fragment, not the union of both: a pill centred on the union lands in the
// gap between the two lines, which reads as "floating in the middle of the square". Null means the
// cell is a single line and Bounds is already the anchor.
internal sealed record ExchangeCell(string Key, Rectangle Bounds, Rectangle? Anchor = null)
{
    public Rectangle AnchorBounds => Anchor ?? Bounds;
}

// The regular exchange view: the currently selected pair read off the slot labels (null = that side
// is empty) and where the "Market Ratio" header sits (badge anchor).
internal sealed record ExchangeMainView(string? WantKey, string? HaveKey, Rectangle? MarketRatioBounds);

// A currency-selection picker: which side it is for, every resolved cell, and the panel bounds.
// Unresolved carries the OCR texts under the header that matched no price key even after the
// wrapped-name merge — purely diagnostic, and the only way to see WHICH items the picker dropped.
// A live round showed cells topping out at ~35 with items visibly missing, and the log could not
// name one of them: it records what resolved, and a name that never resolved leaves no trace at all.
internal sealed record ExchangePickerView(
    ExchangeSide Side,
    IReadOnlyList<ExchangeCell> Cells,
    Rectangle PanelBounds,
    IReadOnlyList<string>? Unresolved = null);

// At most one of Main/Picker is non-null. None = this frame is not the currency exchange.
internal sealed record ExchangeDetection(ExchangeMainView? Main, ExchangePickerView? Picker)
{
    public static readonly ExchangeDetection None = new((ExchangeMainView?)null, null);
    public bool IsNone => Main is null && Picker is null;
}

// Classifies a full-frame OCR pass as the exchange MAIN view, a PICKER, or None, and extracts what
// the engine needs. Pure logic over OcrTextLine + an injected name resolver, so it is fully
// unit-testable with synthetic frames modelled on the real screenshots. Total: garbage in → None.
internal static class ExchangeScreenDetector
{
    // The stylised headers OCR roughly — matched with the same contains-or-Levenshtein approach as
    // the rumour panel signatures. v1 ships English signatures; per-locale strings are a fast-follow
    // (a non-EN client without them leaves the helper quietly idle rather than misfiring).
    private static readonly string[] TitleSignatures = ["currency exchange"];
    private static readonly string[] MarketRatioSignatures = ["market ratio"];
    private static readonly string[] CorroborationSignatures = ["market ratio", "place order"];
    private const string WantLabel = "i want";
    private const string HaveLabel = "i have";
    private const double SignatureThreshold = 0.78;
    // "i want" / "i have" differ by only 3 edits — allow a single slip so they can never cross-match.
    private const int LabelMaxEdits = 1;
    // Slot names sit directly beneath their side label; anything further down (order book) is ignored.
    private const int SlotBandLabelHeights = 5;
    // A picker is only confirmed once this many grid cells resolve — chrome/noise never does.
    private const int MinCellsToConfirm = 3;

    public static ExchangeDetection Detect(IReadOnlyList<OcrTextLine> lines, Func<string, string?> resolve)
    {
        if (lines.Count == 0) return ExchangeDetection.None;

        var title = lines.FirstOrDefault(l => MatchesAny(l.Text, TitleSignatures));
        var wantLabels = lines.Where(l => IsLabel(l.Text, WantLabel)).ToList();
        var haveLabels = lines.Where(l => IsLabel(l.Text, HaveLabel)).ToList();

        if (title is not null)
            return DetectMain(lines, wantLabels, haveLabels, resolve);

        // BOTH side headers and no title: structurally the main view. The title used to be required
        // here, and in a live session that cost us the main view entirely — the stylised "Currency
        // Exchange" heading never OCR'd, so every main-view frame fell through to the picker branch,
        // failed its "exactly one header" test and was classified None. The pair was therefore never
        // remembered and picker badges only worked from a manually-set base. Promoting on the two
        // side headers alone is safe because DetectMain still demands a corroborating "Market Ratio"
        // / "Place Order" line before it returns anything.
        if (wantLabels.Count > 0 && haveLabels.Count > 0)
            return DetectMain(lines, wantLabels, haveLabels, resolve);

        // Picker: exactly ONE side's big header on screen and no main title. Neither → not
        // confidently anything → None.
        if (wantLabels.Count == 0 && haveLabels.Count == 0) return ExchangeDetection.None;
        var side = wantLabels.Count > 0 ? ExchangeSide.Want : ExchangeSide.Have;
        var header = (wantLabels.Count > 0 ? wantLabels : haveLabels).OrderBy(l => l.Bounds.Top).First();
        return DetectPicker(lines, side, header, resolve);
    }

    // Gate helper: does this single OCR line look like any exchange identity text? (title or a side
    // header). Used by the engine's cheap top-band gate.
    public static bool IsAnySignature(string text) =>
        MatchesAny(text, TitleSignatures) || IsLabel(text, WantLabel) || IsLabel(text, HaveLabel);

    private static ExchangeDetection DetectMain(IReadOnlyList<OcrTextLine> lines,
        List<OcrTextLine> wantLabels, List<OcrTextLine> haveLabels, Func<string, string?> resolve)
    {
        // Multi-signature confirm, like the rumour detector: the title alone could be a fluke read.
        if (!lines.Any(l => MatchesAny(l.Text, CorroborationSignatures))) return ExchangeDetection.None;
        if (wantLabels.Count == 0 || haveLabels.Count == 0) return ExchangeDetection.None;

        var want = wantLabels.OrderBy(l => l.Bounds.Top).First();
        var have = haveLabels.OrderBy(l => l.Bounds.Top).First();
        int midX = (Center(want.Bounds).X + Center(have.Bounds).X) / 2;
        bool wantIsLeft = Center(want.Bounds).X < Center(have.Bounds).X;

        string? wantKey = SlotKey(lines, want, wantIsLeft, midX, resolve);
        string? haveKey = SlotKey(lines, have, !wantIsLeft, midX, resolve);

        var ratioLine = lines.FirstOrDefault(l => MatchesAny(l.Text, MarketRatioSignatures));
        return new ExchangeDetection(new ExchangeMainView(wantKey, haveKey, ratioLine?.Bounds), null);
    }

    // The side's selected currency: the topmost resolvable line beneath the side label, on the
    // label's half of the panel, within a few label-heights vertically. The quantity boxes and the
    // ratio value are digits — they never resolve — and the order book sits below the band.
    private static string? SlotKey(IReadOnlyList<OcrTextLine> lines, OcrTextLine label, bool left,
        int midX, Func<string, string?> resolve)
    {
        int window = label.Bounds.Height * SlotBandLabelHeights;
        foreach (var l in lines.OrderBy(l => l.Bounds.Top))
        {
            if (l.Bounds.Top <= label.Bounds.Bottom) continue;
            if (l.Bounds.Top - label.Bounds.Bottom > window) break;
            int cx = Center(l.Bounds).X;
            if (left ? cx >= midX : cx < midX) continue;
            if (resolve(l.Text) is { } key) return key;
        }
        return null;
    }

    private static ExchangeDetection DetectPicker(IReadOnlyList<OcrTextLine> lines, ExchangeSide side,
        OcrTextLine header, Func<string, string?> resolve)
    {
        var below = lines.Where(l => l.Bounds.Top > header.Bounds.Bottom)
                         .OrderBy(l => l.Bounds.Top).ToList();

        var cells = new List<ExchangeCell>();
        var unresolved = new List<OcrTextLine?>();
        foreach (var line in below)
        {
            if (resolve(line.Text) is { } key) cells.Add(new ExchangeCell(key, line.Bounds));
            else unresolved.Add(line);
        }

        // Long names wrap onto two lines inside a cell ("Orb of" / "Augmentation"). Merge vertically
        // adjacent, horizontally overlapping fragments that resolve only as a concatenation. Tabs and
        // section headers never resolve either way, so they fall out here for free.
        for (int i = 0; i < unresolved.Count; i++)
        {
            if (unresolved[i] is not { } a) continue;
            for (int j = i + 1; j < unresolved.Count; j++)
            {
                if (unresolved[j] is not { } b) continue;
                if (!AreWrappedPair(a.Bounds, b.Bounds)) continue;
                if (resolve(a.Text + " " + b.Text) is not { } key) continue;
                // Union for the cell's extent (column matching), but anchor the badge to the LOWER
                // line — a is above b by AreWrappedPair's contract.
                cells.Add(new ExchangeCell(key, Rectangle.Union(a.Bounds, b.Bounds), b.Bounds));
                unresolved[i] = null;
                unresolved[j] = null;
                break;
            }
        }

        // Confirmation: a real picker always shows several known currencies; combat text or a stray
        // header alone never resolves this many.
        if (cells.Count < MinCellsToConfirm) return ExchangeDetection.None;

        cells.Sort((x, y) => x.Bounds.Top != y.Bounds.Top
            ? x.Bounds.Top.CompareTo(y.Bounds.Top)
            : x.Bounds.Left.CompareTo(y.Bounds.Left));
        var panel = Union(cells.Select(c => c.Bounds).Append(header.Bounds));

        // What survived unmatched. Tab labels, section headers and stack counts land here too, so
        // this is noise plus the real misses — but the real misses are only visible here.
        var leftovers = new List<string>();
        foreach (var line in unresolved)
            if (line is { } l) leftovers.Add(l.Text);

        return new ExchangeDetection(null, new ExchangePickerView(side, cells, panel, leftovers));
    }

    // b sits directly beneath a (small gap, tiny tolerance for OCR box overlap) with ≥50% horizontal
    // overlap of the narrower fragment — the shape of a wrapped name inside one grid cell.
    internal static bool AreWrappedPair(Rectangle a, Rectangle b)
    {
        if (b.Top < a.Bottom - 4) return false;
        if (b.Top - a.Bottom > Math.Max(a.Height, b.Height)) return false;
        int overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        return overlap >= Math.Min(a.Width, b.Width) / 2;
    }

    private static bool MatchesAny(string text, string[] signatures)
    {
        var norm = NameNormalizer.Normalize(text);
        if (norm.Length == 0) return false;
        foreach (var sig in signatures)
        {
            if (norm.Contains(sig, StringComparison.Ordinal)) return true;
            int dist = ScanEngine.Levenshtein(norm, sig);
            if (1.0 - (double)dist / Math.Max(norm.Length, sig.Length) >= SignatureThreshold) return true;
        }
        return false;
    }

    private static bool IsLabel(string text, string label)
    {
        var norm = NameNormalizer.Normalize(text);
        if (norm == label) return true;
        return Math.Abs(norm.Length - label.Length) <= 1 && ScanEngine.Levenshtein(norm, label) <= LabelMaxEdits;
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    internal static Rectangle Union(IEnumerable<Rectangle> rects)
    {
        Rectangle? acc = null;
        foreach (var r in rects)
            acc = acc is null ? r : Rectangle.Union(acc.Value, r);
        return acc ?? Rectangle.Empty;
    }
}
