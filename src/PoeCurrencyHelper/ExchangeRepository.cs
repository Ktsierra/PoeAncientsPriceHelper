using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PoeCurrencyHelper;

// One exchange-tradeable item: the API's display name, its value in the league's PRIMARY currency,
// and poe.ninja's volume fields (null when the API omits them).
internal sealed record ExchangeEntry(
    string DisplayName,
    decimal PrimaryValue,
    bool HasMarketData = true,
    decimal? VolumePrimaryValue = null,
    string? MaxVolumeCurrency = null,
    decimal? MaxVolumeRate = null);

// Atomic snapshot of the whole exchange: every fetched category keyed by normalized name, plus the
// league's primary currency ("divine" | "exalted") for volume display. Published as one reference so
// a reader never sees a torn update.
internal sealed record ExchangeSnapshot(
    IReadOnlyDictionary<string, ExchangeEntry> Items,
    string PrimaryCurrency)
{
    public static readonly ExchangeSnapshot Empty = new(
        new ReadOnlyDictionary<string, ExchangeEntry>(new Dictionary<string, ExchangeEntry>()), "divine");
}

// Fetches and holds the poe.ninja currency-exchange snapshot. This is the entire data layer — the app
// has no other input. Carried over from the 3.x price repository, minus the remnant/logbook price view
// and the custom-price override, which only the OCR overlay used.
internal sealed class ExchangeRepository : IDisposable
{
    private readonly HttpClient _http;
    private volatile ExchangeSnapshot _snapshot = ExchangeSnapshot.Empty;
    private System.Threading.Timer? _timer;
    // Cancelled on Dispose so a fetch in flight at shutdown (or one stuck behind the HttpClient
    // timeout) is abandoned cleanly instead of running on against a disposed client.
    private readonly CancellationTokenSource _cts = new();

    // Adaptive refresh cadence: normally every 30 min, but after a failed fetch the timer re-arms
    // every 30s until data comes back — so a launch that hit a transient poe.ninja outage recovers in
    // seconds instead of being stuck behind the full 30-minute interval.
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    private AppConfig? _refreshConfig;

    public ExchangeSnapshot Current => _snapshot;
    public DateTime? LastFetchedAt { get; private set; }
    public int ItemCount => _snapshot.Items.Count;
    // Last failure reason, surfaced in the panel's status line. Null once a fetch succeeds.
    public string? LastError { get; private set; }

    // Raised after every fetch, success or failure, so the panel can redraw. Fires on a thread-pool
    // thread; subscribers must marshal to the UI thread.
    public event Action? Updated;

    // Every poe.ninja PoE2 exchange category the in-game picker can trade. Enumerated against the live
    // API 2026-07-24 ("Runes of Aldur"); picker tabs map by mechanic (Omens->Ritual, Catalysts->Breach,
    // Distilled Emotions->Delirium). Re-verify when a new league adds a category.
    private static readonly string[] ExchangeTypes =
        ["Currency", "Runes", "Expedition", "Verisium", "UncutGems",
         "Fragments", "Essences", "SoulCores", "Breach", "Delirium", "Ritual", "Idols", "Abyss"];

    public ExchangeRepository(HttpClient http) => _http = http;

    public Task<bool> RefreshAsync(AppConfig config) => FetchAsync(config, _cts.Token);

    public void StartAutoRefresh(AppConfig config)
    {
        _refreshConfig = config;
        _timer?.Dispose();
        // Self-rescheduling (period = Infinite): each tick re-arms based on its own outcome, so the
        // cadence adapts between 30 min (healthy) and 30s (retrying).
        _timer = new System.Threading.Timer(RefreshTick, null,
            ItemCount == 0 ? RetryInterval : NormalInterval, Timeout.InfiniteTimeSpan);
    }

    // Timer callback (async void is fine: FetchAsync swallows its own exceptions and never throws).
    private async void RefreshTick(object? _)
    {
        if (_cts.IsCancellationRequested) return;
        bool ok = await FetchAsync(_refreshConfig!, _cts.Token);
        try { _timer?.Change(ok ? NormalInterval : RetryInterval, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* disposed during shutdown — nothing to re-arm */ }
    }

    // Returns true if the fetch published at least one item. On failure the previous good snapshot is
    // left in place — a transient outage must not blank the panel — and LastError is set for the
    // status line. Shutdown cancellation returns false quietly.
    private async Task<bool> FetchAsync(AppConfig config, CancellationToken ct)
    {
        try
        {
            // All categories concurrently: round-trip latency dominates, so this cuts the whole fetch
            // to roughly one request's time.
            var tasks = ExchangeTypes.Select(t => FetchTypeAsync(config.LeagueName, t, ct)).ToList();
            var results = await Task.WhenAll(tasks);

            var items = new Dictionary<string, ExchangeEntry>();
            string primary = "divine";
            foreach (var parsed in results)
            {
                foreach (var (name, entry) in parsed.Items) items[name] = entry;
                if (parsed.Items.Count > 0) primary = parsed.Primary;
            }

            if (items.Count == 0)
            {
                LastError = $"poe.ninja returned nothing for league \"{config.LeagueName}\" — check the league name";
                Updated?.Invoke();
                return false;
            }

            _snapshot = new ExchangeSnapshot(new ReadOnlyDictionary<string, ExchangeEntry>(items), primary);
            LastFetchedAt = DateTime.Now;
            LastError = null;
            Updated?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;   // shutting down — abandon quietly
        }
        catch (Exception ex)
        {
            LastError = $"fetch failed: {ex.Message}";
            Updated?.Invoke();
            return false;
        }
    }

    private async Task<ParsedType> FetchTypeAsync(string league, string type, CancellationToken ct)
    {
        var slug = league.Replace(" ", "").ToLowerInvariant();
        var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(league)}&type={type}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Referer",
            $"https://poe.ninja/poe2/economy/{slug}/{type.ToLowerInvariant()}");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new ParsedType([], "divine");

        return ParseResponse(await resp.Content.ReadAsStringAsync(ct));
    }

    internal sealed record ParsedType(Dictionary<string, ExchangeEntry> Items, string Primary);

    // API shape (exchange/current/overview):
    //   items[]      -> { id, name }          display-name lookup
    //   lines[]      -> { id, primaryValue, volumePrimaryValue, maxVolumeCurrency, maxVolumeRate }
    //   core.primary -> "divine" | "exalted"  which currency primaryValue is denominated in
    // The primary currency differs by league: Softcore prices in divines, Hardcore in exalted (divine
    // is too valuable there), so nothing may assume primaryValue is divines.
    internal static ParsedType ParseResponse(string json)
    {
        var items = new Dictionary<string, ExchangeEntry>();
        string primary = "divine";
        try
        {
            var obj = JObject.Parse(json);

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (obj["items"] is JArray itemsArr)
                foreach (var item in itemsArr)
                {
                    var id = item["id"]?.Value<string>();
                    var name = item["name"]?.Value<string>();
                    if (id is not null && name is not null) nameMap[id] = name;
                }

            primary = obj["core"]?["primary"]?.Value<string>() ?? "divine";

            if (obj["lines"] is not JArray lines) return new ParsedType(items, primary);
            foreach (var line in lines)
            {
                var id = line["id"]?.Value<string>();
                if (id is null || !nameMap.TryGetValue(id, out var name)) continue;
                var key = NameNormalizer.Normalize(name);
                if (string.IsNullOrEmpty(key)) continue;

                // Items with a null primaryValue exist in poe.ninja but have no trading data. Keep
                // them (HasMarketData=false) so the panel can say "no data" rather than silently
                // omitting a currency the player is looking for.
                var pv = line["primaryValue"];
                if (pv is null || pv.Type == JTokenType.Null)
                {
                    items[key] = new ExchangeEntry(name, 0m, HasMarketData: false);
                    continue;
                }

                items[key] = new ExchangeEntry(name, pv.Value<decimal>(), true,
                    Num(line["volumePrimaryValue"]),
                    line["maxVolumeCurrency"]?.Value<string>(),
                    Num(line["maxVolumeRate"]));
            }
        }
        catch
        {
            // A malformed category is skipped, never fatal — the other twelve still fill the panel.
        }
        return new ParsedType(items, primary);

        static decimal? Num(JToken? t) => t is null || t.Type == JTokenType.Null ? null : t.Value<decimal>();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer?.Dispose();
        _timer = null;
        _cts.Dispose();
    }
}
