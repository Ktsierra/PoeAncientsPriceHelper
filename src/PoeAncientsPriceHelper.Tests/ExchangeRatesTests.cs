using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

public class ExchangeRatesTests
{
    // Values from the live API snapshot the design was verified against.
    private static readonly ExchangeEntry Divine =
        new("Divine Orb", 1m, true, 82025m, "chaos", 0.1086m);
    private static readonly ExchangeEntry Exalted =
        new("Exalted Orb", 0.002032m, true, 7544m, "divine", 492.2m);
    private static readonly ExchangeEntry Chaos =
        new("Chaos Orb", 0.1086m, true, 82025m, "divine", 9.21m);
    private static readonly ExchangeEntry NoData =
        new("Mystery Item", 0m, HasMarketData: false);

    // Browsing the "I Want" picker with Divine settled as "I Have": the exalted cell's max-volume
    // counter IS divine, so poe.ninja's observed pair rate (492.2 exalts per divine) is used directly.
    [Fact]
    public void RatioFor_DirectPairRate_CellIsWant()
    {
        var s = ExchangeRates.RatioFor(Exalted, Divine, "divine orb", cellIsWant: true);
        Assert.Equal("492 : 1", s);
    }

    // Same pair, browsing the "I Have" picker with Divine settled as "I Want": rate inverts.
    [Fact]
    public void RatioFor_DirectPairRate_CellIsHave()
    {
        var s = ExchangeRates.RatioFor(Exalted, Divine, "divine orb", cellIsWant: false);
        Assert.Equal("1 : 492", s);
    }

    // Opposite is NOT the cell's max-volume counter → derived from primary values.
    // want=chaos (0.1086 div) vs have=exalted (0.002032 div): 1 chaos costs ~53.4 exalts.
    [Fact]
    public void RatioFor_DerivedFromPrimaryValues()
    {
        var s = ExchangeRates.RatioFor(Chaos, Exalted, "exalted orb", cellIsWant: true);
        Assert.Equal("1 : 53", s);
    }

    [Fact]
    public void RatioFor_NoMarketData_ReturnsNull()
    {
        Assert.Null(ExchangeRates.RatioFor(NoData, Divine, "divine orb", cellIsWant: true));
        Assert.Null(ExchangeRates.RatioFor(Divine, NoData, "mystery item", cellIsWant: true));
    }

    [Fact]
    public void RatioFor_ZeroValue_ReturnsNull()
    {
        var zero = new ExchangeEntry("Zero", 0m);
        Assert.Null(ExchangeRates.RatioFor(zero, Divine, "divine orb", cellIsWant: true));
    }

    // Small ratios keep two decimals like the game's "1 : 3.20"; ≥10 rounds to integers;
    // ≥1000 uses the game's "2.1k" style (CLAUDE.md rule 3).
    [Theory]
    [InlineData("3.2", 3.2049)]
    [InlineData("1", 1.004)]
    [InlineData("73", 73.4)]
    [InlineData("492", 492.2)]
    [InlineData("2.1k", 2100)]
    [InlineData("49k", 49019)]
    public void FormatSide_Tiers(string expected, decimal n)
    {
        Assert.Equal(expected, ExchangeRates.FormatSide(n));
    }

    [Fact]
    public void FormatRatio_OrientsDearerSideToOne()
    {
        Assert.Equal("492 : 1", ExchangeRates.FormatRatio(492.2m));
        Assert.Equal("1 : 492", ExchangeRates.FormatRatio(1m / 492.2m));
        Assert.Null(ExchangeRates.FormatRatio(0m));
    }

    [Theory]
    [InlineData("82k", 82025)]
    [InlineData("1.8k", 1759)]
    [InlineData("181", 180.7)]
    [InlineData("11", 10.77)]
    [InlineData("0.5", 0.5445)]
    public void Abbrev_Compacts(string expected, decimal v)
    {
        Assert.Equal(expected, ExchangeRates.Abbrev(v));
    }

    [Fact]
    public void FormatVolume_UsesPrimaryUnit()
    {
        Assert.Equal("82k div", ExchangeRates.FormatVolume(82025m, "divine"));
        Assert.Equal("12k ex", ExchangeRates.FormatVolume(12100m, "exalted"));
        Assert.Null(ExchangeRates.FormatVolume(null, "divine"));
        Assert.Null(ExchangeRates.FormatVolume(0m, "divine"));
    }
}
