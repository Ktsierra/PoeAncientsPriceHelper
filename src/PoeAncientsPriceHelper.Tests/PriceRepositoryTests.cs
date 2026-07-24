using System.IO;
using System.Net;
using System.Net.Http;
using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

public class PriceRepositoryTests
{
    // Real API shape: items[] has id+name, lines[] has id+primaryValue, core.rates.exalted
    private const string FakeApiResponse = """
        {
          "items": [
            { "id": "chilling-flux",            "name": "Chilling Flux" },
            { "id": "support-scattering-flame",  "name": "Support: Scattering Flame" }
          ],
          "lines": [
            { "id": "chilling-flux",           "primaryValue": 0.5 },
            { "id": "support-scattering-flame", "primaryValue": 1.2 }
          ],
          "core": { "primary": "divine", "rates": { "exalted": 80.0 } }
        }
        """;

    // Hardcore shape: core.primary == "exalted", so primaryValue is in exalted and rates carries
    // "divine" (divines per exalted) instead of "exalted".
    private const string FakeHardcoreResponse = """
        {
          "items": [
            { "id": "orb-of-alchemy", "name": "Orb of Alchemy" },
            { "id": "divine-orb",     "name": "Divine Orb" }
          ],
          "lines": [
            { "id": "orb-of-alchemy", "primaryValue": 1.13 },
            { "id": "divine-orb",     "primaryValue": 67.51 }
          ],
          "core": { "primary": "exalted", "rates": { "divine": 0.01481, "chaos": 0.2785 } }
        }
        """;

    private static AppConfig DefaultConfig(string tempDir) => new()
    {
        LeagueName = "Test League",
        CustomPricesPath = Path.Combine(tempDir, "custom_prices.json")
    };

    [Fact]
    public async Task FetchPopulatesDict_WithNormalizedKeys()
    {
        using var http = FakeHttp(FakeApiResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.True(repo.Prices.ContainsKey("chilling flux"));
        Assert.True(repo.Prices.ContainsKey("support scattering flame"));
        Assert.Equal(0.5m, repo.Prices["chilling flux"].DivineValue);
        Assert.Equal(40.0m, repo.Prices["chilling flux"].ExaltedValue); // 0.5 * 80
    }

    [Fact]
    public async Task ExaltedPrimary_DenominatesInExalted_NotDivine()
    {
        using var http = FakeHttp(FakeHardcoreResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        // 1.13 exalted → ExaltedValue 1.1, DivineValue 1.13*0.01481 < 1 ⇒ shown with the exalted icon.
        var alch = repo.Prices["orb of alchemy"];
        Assert.Equal(1.1m, alch.ExaltedValue);
        Assert.True(alch.DivineValue < 1m, $"expected <1 divine, got {alch.DivineValue}");

        // Pricey item still resolves to >=1 divine ⇒ shown with the divine icon.
        Assert.True(repo.Prices["divine orb"].DivineValue >= 0.99m);
    }

    [Fact]
    public async Task CustomOverride_ReplacesPoENinjaEntry()
    {
        using var http = FakeHttp(FakeApiResponse);
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "custom_prices.json"),
            """{"chilling flux":{"divineValue":2.0,"exaltedValue":160.0}}""");

        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.Equal(2.0m, repo.Prices["chilling flux"].DivineValue);
    }

    [Fact]
    public async Task CustomOverride_InsertsNewEntry()
    {
        using var http = FakeHttp("""{"items":[],"lines":[],"core":{"rates":{"exalted":80}}}""");
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "custom_prices.json"),
            """{"support scattering flame":{"divineValue":1.5,"exaltedValue":120.0}}""");

        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.True(repo.Prices.ContainsKey("support scattering flame"));
        Assert.Equal(1.5m, repo.Prices["support scattering flame"].DivineValue);
    }

    [Fact]
    public async Task MissingCustomFile_IsIgnoredSilently()
    {
        using var http = FakeHttp(FakeApiResponse);
        var config = new AppConfig
        {
            LeagueName = "Test League",
            CustomPricesPath = "/nonexistent/path/custom_prices.json"
        };
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(config);
        Assert.True(repo.Prices.ContainsKey("chilling flux"));
    }

    // The league name (e.g. the "HC Runes of Aldur" Hardcore variant) is used verbatim — URL-escaped —
    // as poe.ninja's API league param, and slugged into the Referer.
    [Theory]
    [InlineData("Runes of Aldur", "league=Runes%20of%20Aldur&", "/economy/runesofaldur/")]
    [InlineData("HC Runes of Aldur", "league=HC%20Runes%20of%20Aldur&", "/economy/hcrunesofaldur/")]
    public async Task LeagueName_DrivesApiParamAndReferer(string league, string expectedParam, string expectedSlug)
    {
        var handler = new CapturingFakeHttpHandler(FakeApiResponse);
        using var http = new HttpClient(handler);
        using var dir = new TempDir();
        var config = DefaultConfig(dir.Path);
        config.LeagueName = league;

        await new PriceRepository(http).InitialFetchAsync(config);

        Assert.All(handler.Urls, u => Assert.Contains(expectedParam, u));
        // Referer's trailing segment is the per-type slug; assert only the league-slug segment.
        Assert.All(handler.Referers, r => Assert.Contains(expectedSlug, r));
    }

    // Items that appear on poe.ninja but have no trading data come back with primaryValue: null.
    // They should be stored with HasMarketData: false so the overlay can show "no info" rather
    // than hiding the row entirely — the user should know the item was recognised.
    [Fact]
    public async Task NullPrimaryValue_SetsHasMarketDataFalse()
    {
        const string response = """
            {
              "items": [
                { "id": "chilling-flux", "name": "Chilling Flux" },
                { "id": "mystery-item",  "name": "Mystery Item" }
              ],
              "lines": [
                { "id": "chilling-flux", "primaryValue": 0.5 },
                { "id": "mystery-item",  "primaryValue": null }
              ],
              "core": { "primary": "divine", "rates": { "exalted": 80.0 } }
            }
            """;

        using var http = FakeHttp(response);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.True(repo.Prices.ContainsKey("mystery item"));
        var entry = repo.Prices["mystery item"];
        Assert.False(entry.HasMarketData);
        Assert.Equal(0m, entry.DivineValue);
        Assert.Equal(0m, entry.ExaltedValue);

        // The item with real data is unaffected.
        Assert.True(repo.Prices["chilling flux"].HasMarketData);
    }

    // custom_prices.json keys are run through NameNormalizer so users can write display names
    // ("Chilling Flux") instead of pre-normalised keys ("chilling flux") without breaking lookups.
    [Fact]
    public async Task CustomOverride_AcceptsUnnormalizedKey()
    {
        using var http = FakeHttp(FakeApiResponse);
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "custom_prices.json"),
            """{"Chilling Flux":{"divineValue":3.0,"exaltedValue":240.0}}""");

        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.Equal(3.0m, repo.Prices["chilling flux"].DivineValue);
    }

    // Malformed custom_prices.json must not crash the app — the error is logged and the
    // poe.ninja prices are kept as-is.
    [Fact]
    public async Task CustomOverride_MalformedJson_IsIgnoredSilently()
    {
        using var http = FakeHttp(FakeApiResponse);
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "custom_prices.json"), "{ not valid json !!!");

        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        // poe.ninja data survives the bad override file.
        Assert.True(repo.Prices.ContainsKey("chilling flux"));
        Assert.Equal(0.5m, repo.Prices["chilling flux"].DivineValue);
    }

    // A response missing the `core` block (e.g. an API change or truncation) must not throw.
    // With no core, `primary` falls back to "divine", so divinePerPrimary is 1 and the price is
    // taken verbatim from primaryValue — the item is still usable, not zeroed or dropped.
    [Fact]
    public async Task MissingCoreBlock_FallsBackToDivinePrimary()
    {
        const string response = """
            {
              "items": [{ "id": "chilling-flux", "name": "Chilling Flux" }],
              "lines": [{ "id": "chilling-flux", "primaryValue": 0.5 }]
            }
            """;

        using var http = FakeHttp(response);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        var ex = await Record.ExceptionAsync(() => repo.InitialFetchAsync(DefaultConfig(dir.Path)));
        Assert.Null(ex);

        // Core absent → primary defaults to "divine" → DivineValue == primaryValue.
        Assert.True(repo.Prices.ContainsKey("chilling flux"));
        Assert.Equal(0.5m, repo.Prices["chilling flux"].DivineValue);
    }

    [Theory]
    [InlineData("Support: Scattering Flame", "support scattering flame")]
    [InlineData("CHILLING FLUX", "chilling flux")]
    [InlineData("  Grip's Edge  ", "grip s edge")]
    [InlineData("Rune-of-Aldur", "rune of aldur")]
    public void NormalizeName_ProducesConsistentKey(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    private static HttpClient FakeHttp(string responseJson)
    {
        var handler = new FakeHttpMessageHandler(responseJson);
        return new HttpClient(handler);
    }

    // ---- Currency Exchange helper: exchange snapshot (volume fields + category segregation) ----

    // Real poe.ninja PoE2 shape incl. the volume fields (verified live 2026-07-24).
    private const string FakeExchangeCurrencyResponse = """
        {
          "items": [
            { "id": "divine",  "name": "Divine Orb" },
            { "id": "exalted", "name": "Exalted Orb" },
            { "id": "mystery", "name": "Mystery Item" }
          ],
          "lines": [
            { "id": "divine",  "primaryValue": 1,
              "volumePrimaryValue": 82025, "maxVolumeCurrency": "chaos", "maxVolumeRate": 0.1086 },
            { "id": "exalted", "primaryValue": 0.002032,
              "volumePrimaryValue": 7544, "maxVolumeCurrency": "divine", "maxVolumeRate": 492.2 },
            { "id": "mystery", "primaryValue": null }
          ],
          "core": { "primary": "divine", "rates": { "exalted": 492.2, "chaos": 9.21 } }
        }
        """;

    private const string FakeEssenceResponse = """
        {
          "items": [ { "id": "essence-of-the-body", "name": "Essence of the Body" } ],
          "lines": [ { "id": "essence-of-the-body", "primaryValue": 0.02,
                       "volumePrimaryValue": 12.5, "maxVolumeCurrency": "exalted", "maxVolumeRate": 9.8 } ],
          "core": { "primary": "divine", "rates": { "exalted": 492.2 } }
        }
        """;

    // Routes by the `type=` query param so remnant vs exchange-only categories return different items.
    private sealed class RoutingFakeHandler(Func<string, string> jsonForUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonForUrl(request.RequestUri!.ToString()))
            });
    }

    [Fact]
    public async Task ExchangeSnapshot_CarriesVolumeFieldsAndPrimary()
    {
        using var http = FakeHttp(FakeExchangeCurrencyResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        var ex = repo.Exchange;
        Assert.Equal("divine", ex.PrimaryCurrency);
        var divine = ex.Items["divine orb"];
        Assert.Equal("Divine Orb", divine.DisplayName);
        Assert.Equal(1m, divine.PrimaryValue);
        Assert.Equal(82025m, divine.VolumePrimaryValue);
        Assert.Equal("chaos", divine.MaxVolumeCurrency);
        Assert.Equal(0.1086m, divine.MaxVolumeRate);
        Assert.True(divine.HasMarketData);
    }

    [Fact]
    public async Task ExchangeSnapshot_NullPrimaryValue_KeepsItemWithoutMarketData()
    {
        using var http = FakeHttp(FakeExchangeCurrencyResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        var mystery = repo.Exchange.Items["mystery item"];
        Assert.False(mystery.HasMarketData);
        Assert.Null(mystery.VolumePrimaryValue);
    }

    [Fact]
    public async Task ExchangeSnapshot_MissingVolumeFields_AreNull()
    {
        // FakeApiResponse (top of file) has no volume fields at all — they must parse as null.
        using var http = FakeHttp(FakeApiResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        var flux = repo.Exchange.Items["chilling flux"];
        Assert.Null(flux.VolumePrimaryValue);
        Assert.Null(flux.MaxVolumeCurrency);
        Assert.Null(flux.MaxVolumeRate);
    }

    // CLAUDE.md: extra exchange categories must NOT widen the remnant matching surface. An
    // essence (exchange-only type) appears in the exchange view but never in the remnant Prices.
    [Fact]
    public async Task RemnantView_ExcludesExchangeOnlyCategories()
    {
        using var http = new HttpClient(new RoutingFakeHandler(url =>
            url.Contains("type=Essences") ? FakeEssenceResponse : FakeExchangeCurrencyResponse));
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        Assert.True(repo.Exchange.Items.ContainsKey("essence of the body"));
        Assert.False(repo.Prices.ContainsKey("essence of the body"));
        Assert.True(repo.Prices.ContainsKey("divine orb"));   // remnant types still populate Prices
    }

    [Fact]
    public async Task ExchangeSnapshot_KeysByLength_IndexesEveryKey()
    {
        using var http = FakeHttp(FakeExchangeCurrencyResponse);
        using var dir = new TempDir();
        var repo = new PriceRepository(http);
        await repo.InitialFetchAsync(DefaultConfig(dir.Path));

        var ex = repo.Exchange;
        foreach (var key in ex.Items.Keys)
            Assert.Contains(key, ex.KeysByLength[key.Length]);
    }
}
