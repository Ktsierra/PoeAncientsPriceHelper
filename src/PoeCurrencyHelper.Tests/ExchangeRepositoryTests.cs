using System.Linq;
using PoeCurrencyHelper;

namespace PoeCurrencyHelper.Tests;

// Parsing of poe.ninja's exchange/current/overview payload. Shapes here mirror the live API as
// observed on 2026-07-27 for "Runes of Aldur" — including the real Exalted and Divine lines, since a
// misparse there silently mis-prices the base currency everything else is quoted against.
public class ExchangeRepositoryTests
{
    private const string RealisticJson = """
    {
      "core": {
        "primary": "divine",
        "secondary": "chaos",
        "rates": { "exalted": 440.9, "chaos": 8.80 }
      },
      "items": [
        { "id": "divine",  "name": "Divine Orb" },
        { "id": "exalted", "name": "Exalted Orb" },
        { "id": "chaos",   "name": "Chaos Orb" },
        { "id": "mystery", "name": "Mystery Item" }
      ],
      "lines": [
        { "id": "divine",  "primaryValue": 1,        "volumePrimaryValue": 68919, "maxVolumeCurrency": "chaos",  "maxVolumeRate": 0.1137 },
        { "id": "exalted", "primaryValue": 0.002268, "volumePrimaryValue": 6481,  "maxVolumeCurrency": "divine", "maxVolumeRate": 440.9 },
        { "id": "chaos",   "primaryValue": 0.1137,   "volumePrimaryValue": 68919, "maxVolumeCurrency": "divine", "maxVolumeRate": 8.80 },
        { "id": "mystery", "primaryValue": null }
      ]
    }
    """;

    [Fact]
    public void ParsesEveryLineKeyedByNormalizedName()
    {
        var parsed = ExchangeRepository.ParseResponse(RealisticJson);
        Assert.Equal(4, parsed.Items.Count);
        Assert.True(parsed.Items.ContainsKey("divine orb"));
        Assert.True(parsed.Items.ContainsKey("exalted orb"));
        Assert.True(parsed.Items.ContainsKey("chaos orb"));
    }

    [Fact]
    public void KeepsTheApiDisplayName()
    {
        var parsed = ExchangeRepository.ParseResponse(RealisticJson);
        Assert.Equal("Exalted Orb", parsed.Items["exalted orb"].DisplayName);
    }

    [Fact]
    public void ReadsPrimaryCurrencyFromCore()
    {
        Assert.Equal("divine", ExchangeRepository.ParseResponse(RealisticJson).Primary);
    }

    // Hardcore prices in exalted, so nothing may assume primaryValue is divines.
    [Fact]
    public void HonoursAnExaltedPrimaryLeague()
    {
        var json = RealisticJson.Replace("\"primary\": \"divine\"", "\"primary\": \"exalted\"");
        Assert.Equal("exalted", ExchangeRepository.ParseResponse(json).Primary);
    }

    [Fact]
    public void ReadsVolumeAndMaxVolumePairFields()
    {
        var exalted = ExchangeRepository.ParseResponse(RealisticJson).Items["exalted orb"];
        Assert.Equal(0.002268m, exalted.PrimaryValue);
        Assert.Equal(6481m, exalted.VolumePrimaryValue);
        Assert.Equal("divine", exalted.MaxVolumeCurrency);
        Assert.Equal(440.9m, exalted.MaxVolumeRate);
    }

    // Items with no trading data are kept (flagged), so the panel can show "no data" rather than
    // silently omitting a currency the player is looking for.
    [Fact]
    public void KeepsNullValuedItemsFlaggedAsHavingNoMarketData()
    {
        var mystery = ExchangeRepository.ParseResponse(RealisticJson).Items["mystery item"];
        Assert.False(mystery.HasMarketData);
        Assert.Equal(0m, mystery.PrimaryValue);
    }

    [Fact]
    public void MissingOptionalVolumeFieldsAreNullNotZero()
    {
        const string json = """
        {
          "core": { "primary": "divine" },
          "items": [ { "id": "divine", "name": "Divine Orb" } ],
          "lines": [ { "id": "divine", "primaryValue": 1 } ]
        }
        """;
        var entry = ExchangeRepository.ParseResponse(json).Items["divine orb"];
        Assert.Null(entry.VolumePrimaryValue);
        Assert.Null(entry.MaxVolumeCurrency);
        Assert.Null(entry.MaxVolumeRate);
    }

    // A line whose id has no matching items[] entry has no display name, so it cannot be shown.
    [Fact]
    public void SkipsLinesWithNoMatchingItemEntry()
    {
        const string json = """
        {
          "core": { "primary": "divine" },
          "items": [ { "id": "divine", "name": "Divine Orb" } ],
          "lines": [ { "id": "divine", "primaryValue": 1 }, { "id": "ghost", "primaryValue": 5 } ]
        }
        """;
        var parsed = ExchangeRepository.ParseResponse(json);
        Assert.Single(parsed.Items);
        Assert.True(parsed.Items.ContainsKey("divine orb"));
    }

    // One malformed category must never take down the other twelve.
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{ \"lines\": [] }")]
    [InlineData("{ \"core\": { \"primary\": \"divine\" }, \"items\": [], \"lines\": null }")]
    public void MalformedPayloadsYieldEmptyInsteadOfThrowing(string json)
    {
        var parsed = ExchangeRepository.ParseResponse(json);
        Assert.Empty(parsed.Items);
        Assert.Equal("divine", parsed.Primary);
    }

    // End-to-end through the ratio layer: the numbers a player actually reads in the panel.
    [Fact]
    public void ParsedEntriesFeedTheRatioLayerCorrectly()
    {
        var items = ExchangeRepository.ParseResponse(RealisticJson).Items;
        var ratio = ExchangeRates.RatioFor(
            items["exalted orb"], items["divine orb"], "divine orb", cellIsWant: true);
        Assert.Equal("441 : 1 div", ratio);
    }

    // Units moved, against the real API figures for Runes of Aldur on 2026-07-28. The whole point of
    // the column: chaos and mirrors move a comparable number of DIVINES while the stock changing
    // hands differs by four orders of magnitude.
    [Fact]
    public void UnitsMovedSeparatesStockFromValue()
    {
        var chaos = new ExchangeEntry("Chaos Orb", 0.1144m, true, VolumePrimaryValue: 49116m);
        var mirror = new ExchangeEntry("Mirror of Kalandra", 4856m, true, VolumePrimaryValue: 80126m);

        var chaosUnits = ExchangeRates.UnitsMoved(chaos);
        var mirrorUnits = ExchangeRates.UnitsMoved(mirror);

        Assert.NotNull(chaosUnits);
        Assert.NotNull(mirrorUnits);
        Assert.Equal(429_336m, Math.Round(chaosUnits!.Value));
        Assert.Equal(16.5m, Math.Round(mirrorUnits!.Value, 1));

        // Mirrors move MORE divines than chaos here, yet ~26,000x less stock.
        Assert.True(mirror.VolumePrimaryValue > chaos.VolumePrimaryValue);
        Assert.True(mirrorUnits < chaosUnits);
    }

    // Display rounds to whole items above 10 — the derived count is fractional (16.5 mirrors) only
    // because it comes from a value aggregate, and half a mirror is not a thing anyone trades.
    [Fact]
    public void UnitsMovedFormatsCompactly()
    {
        var chaos = new ExchangeEntry("Chaos Orb", 0.1144m, true, VolumePrimaryValue: 49116m);
        var mirror = new ExchangeEntry("Mirror of Kalandra", 4856m, true, VolumePrimaryValue: 80126m);
        Assert.Equal("429k", ExchangeRates.FormatUnits(ExchangeRates.UnitsMoved(chaos)));
        Assert.Equal("17", ExchangeRates.FormatUnits(ExchangeRates.UnitsMoved(mirror)));
    }

    // Never divide by zero, and never invent a count for an item with no usable data.
    [Theory]
    [InlineData(0, 100, false)]     // no unit price -> undefined
    [InlineData(5, 0, false)]       // no volume
    [InlineData(5, 100, true)]
    public void UnitsMovedIsNullWhenItCannotBeDerived(int price, int volume, bool expected)
    {
        var entry = new ExchangeEntry("X", price, true, VolumePrimaryValue: volume);
        Assert.Equal(expected, ExchangeRates.UnitsMoved(entry) is not null);
    }

    [Fact]
    public void UnitsMovedIsNullWithoutMarketData()
    {
        var entry = new ExchangeEntry("X", 0m, HasMarketData: false, VolumePrimaryValue: 100m);
        Assert.Null(ExchangeRates.UnitsMoved(entry));
        Assert.Null(ExchangeRates.FormatUnits(null));
    }
}
