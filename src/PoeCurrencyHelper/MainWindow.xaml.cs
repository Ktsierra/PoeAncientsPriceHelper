using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace PoeCurrencyHelper;

// One row of the table.
internal sealed record RatioRow(string Name, string Ratio, string Volume, string Units);

// The whole product: pick a league and a base currency, see every tradeable currency priced against
// it. No screen capture, no OCR, nothing anchored to the game window — which is precisely why it
// survives scrolling, tab-switching and dense cells, all of which killed the 3.x in-game overlay.
public partial class MainWindow : Window
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        // Happy Eyeballs: poe.ninja resolves to both A and AAAA records, and on a broken-IPv6 network
        // the default connect stalls for the full timeout. Race the families instead.
        ConnectCallback = (ctx, ct) => HappyEyeballs.ConnectAsync(ctx.DnsEndPoint, ct),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    private readonly AppConfig _config;
    private readonly ExchangeRepository _repo;
    private bool _loaded;

    private static readonly (string Key, string Label)[] SortModes =
    [
        ("volume", "Most value"),
        ("units", "Most units"),
        ("ratio", "Cheapest first"),
        ("name", "Name (A-Z)"),
    ];

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigStore.Load();
        _repo = new ExchangeRepository(_http);
        _repo.Updated += OnRepoUpdated;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RestorePlacement();

        LeagueBox.ItemsSource = _config.AvailableLeagues;
        LeagueBox.SelectedItem = _config.AvailableLeagues.Contains(_config.LeagueName)
            ? _config.LeagueName : _config.AvailableLeagues[0];

        SortBox.ItemsSource = SortModes.Select(m => m.Label).ToList();
        SortBox.SelectedIndex = Math.Max(0, Array.FindIndex(SortModes, m => m.Key == _config.SortBy));

        TopmostBox.IsChecked = _config.AlwaysOnTop;
        HideNoDataBox.IsChecked = _config.HideNoData;
        Topmost = _config.AlwaysOnTop;

        _loaded = true;
        SetStatus("Loading market data from poe.ninja...");
        await _repo.RefreshAsync(_config);
        _repo.StartAutoRefresh(_config);
    }

    // Repo events arrive on a thread-pool thread.
    private void OnRepoUpdated() => Dispatcher.Invoke(() => { PopulateBases(); Render(); });

    // Fill the base dropdown from the live snapshot, so it only ever offers currencies that actually
    // exist this league. Defaults to the league's primary (divine in Softcore, exalted in Hardcore),
    // which is what almost everyone prices against.
    private void PopulateBases()
    {
        var snapshot = _repo.Current;
        if (snapshot.Items.Count == 0) return;

        var keys = snapshot.Items
            .Where(kv => kv.Value.HasMarketData)
            .OrderBy(kv => kv.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();

        if (_config.BaseCurrency.Length == 0 || !snapshot.Items.ContainsKey(_config.BaseCurrency))
            _config.BaseCurrency = DefaultBaseFor(snapshot, keys);

        var display = keys.Select(k => snapshot.Items[k].DisplayName).ToList();
        if (BaseBox.ItemsSource is not List<string> current || !current.SequenceEqual(display))
            BaseBox.ItemsSource = display;

        if (snapshot.Items.TryGetValue(_config.BaseCurrency, out var baseEntry))
            BaseBox.SelectedItem = baseEntry.DisplayName;
    }

    private static string DefaultBaseFor(ExchangeSnapshot snapshot, List<string> keys)
    {
        var preferred = snapshot.PrimaryCurrency.Equals("exalted", StringComparison.OrdinalIgnoreCase)
            ? "exalted orb" : "divine orb";
        return snapshot.Items.ContainsKey(preferred) ? preferred : keys.FirstOrDefault() ?? "";
    }

    private void Render()
    {
        var snapshot = _repo.Current;
        if (snapshot.Items.Count == 0)
        {
            Table.ItemsSource = null;
            SetStatus(_repo.LastError ?? "No market data yet.");
            return;
        }

        if (!snapshot.Items.TryGetValue(_config.BaseCurrency, out var baseEntry))
        {
            Table.ItemsSource = null;
            SetStatus("Pick a base currency.");
            return;
        }

        var filter = FilterBox.Text.Trim();
        var rows = new List<Ranked>();
        foreach (var (key, entry) in snapshot.Items)
        {
            if (key == _config.BaseCurrency) continue;                       // 1:1 against itself
            if (_config.HideNoData && !entry.HasMarketData) continue;
            if (filter.Length > 0 &&
                entry.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            // cellIsWant: true — the table reads "N of this per 1 base", the direction a player
            // thinks in when spending the base currency.
            var ratio = entry.HasMarketData
                ? ExchangeRates.RatioFor(entry, baseEntry, _config.BaseCurrency, cellIsWant: true)
                : null;
            var volume = ExchangeRates.FormatVolume(entry.VolumePrimaryValue, snapshot.PrimaryCurrency);
            var units = ExchangeRates.UnitsMoved(entry);
            rows.Add(new Ranked(
                new RatioRow(entry.DisplayName, ratio ?? "no data", volume ?? "",
                             ExchangeRates.FormatUnits(units) ?? ""),
                entry.VolumePrimaryValue ?? 0m,
                units ?? 0m));
        }

        Table.ItemsSource = SortRows(rows, snapshot).Select(r => r.Row).ToList();
        SetStatus(StatusLine(snapshot));
    }

    // A row plus the raw numbers it sorts by — sorting the formatted strings would order "9" after
    // "429k" and put "1 : 4.9k" next to "1 : 12".
    private sealed record Ranked(RatioRow Row, decimal Volume, decimal Units);

    private List<Ranked> SortRows(List<Ranked> rows, ExchangeSnapshot snapshot) => _config.SortBy switch
    {
        // Cheapest first: sort by the item's own value in the league's primary currency, not by the
        // formatted ratio string, which is normalized and would sort as text.
        "ratio" => rows.OrderBy(r => ValueOf(r.Row.Name, snapshot)).ToList(),
        "name" => rows.OrderBy(r => r.Row.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        "units" => rows.OrderByDescending(r => r.Units).ToList(),
        _ => rows.OrderByDescending(r => r.Volume).ToList(),
    };

    private static decimal ValueOf(string displayName, ExchangeSnapshot snapshot)
    {
        var key = NameNormalizer.Normalize(displayName);
        return snapshot.Items.TryGetValue(key, out var e) ? e.PrimaryValue : decimal.MaxValue;
    }

    private string StatusLine(ExchangeSnapshot snapshot)
    {
        if (_repo.LastError is { } err) return $"{err} (showing last good data)";
        var baseName = snapshot.Items.TryGetValue(_config.BaseCurrency, out var b)
            ? b.DisplayName : _config.BaseCurrency;
        var age = _repo.LastFetchedAt is { } at ? Age(DateTime.Now - at) : "unknown age";
        return $"{snapshot.Items.Count} currencies priced against {baseName} — snapshot {age} old";
    }

    private static string Age(TimeSpan span) =>
        span.TotalMinutes < 1 ? "less than a minute"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m"
        : $"{(int)span.TotalHours}h {(int)(span.TotalMinutes % 60)}m";

    private void SetStatus(string text) => StatusText.Text = text;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        SetStatus("Refreshing...");
        await _repo.RefreshAsync(_config);
        RefreshButton.IsEnabled = true;
    }

    private async void LeagueBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || LeagueBox.SelectedItem is not string league || league == _config.LeagueName) return;
        _config.LeagueName = league;
        // The base is league-scoped (Hardcore prices in exalted), so let it re-resolve on new data.
        _config.BaseCurrency = "";
        Save();
        SetStatus($"Loading {league}...");
        await _repo.RefreshAsync(_config);
    }

    private void BaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || BaseBox.SelectedItem is not string display) return;
        var key = NameNormalizer.Normalize(display);
        if (key == _config.BaseCurrency || !_repo.Current.Items.ContainsKey(key)) return;
        _config.BaseCurrency = key;
        Save();
        Render();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SortBox.SelectedIndex < 0) return;
        _config.SortBy = SortModes[SortBox.SelectedIndex].Key;
        Save();
        Render();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded) Render();
    }

    private void TopmostBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _config.AlwaysOnTop = TopmostBox.IsChecked == true;
        Topmost = _config.AlwaysOnTop;
        Save();
    }

    private void HideNoDataBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _config.HideNoData = HideNoDataBox.IsChecked == true;
        Save();
        Render();
    }

    // Reopen where the player left it, but only if that position is still on a connected monitor —
    // otherwise a panel parked on a since-disconnected second screen would open off-screen.
    private void RestorePlacement()
    {
        if (_config.WindowWidth > 0) Width = _config.WindowWidth;
        if (_config.WindowHeight > 0) Height = _config.WindowHeight;
        if (_config.WindowX < 0 || _config.WindowY < 0) { WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }

        var virtualBounds = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!virtualBounds.Contains(new Point(_config.WindowX + 40, _config.WindowY + 20)))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        Left = _config.WindowX;
        Top = _config.WindowY;
    }

    private void Save()
    {
        try { ConfigStore.Save(_config); }
        catch { /* a failed settings write must never interrupt the panel */ }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _config.WindowX = Left;
            _config.WindowY = Top;
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
        }
        Save();
        _repo.Updated -= OnRepoUpdated;
        _repo.Dispose();
        _http.Dispose();
    }
}
