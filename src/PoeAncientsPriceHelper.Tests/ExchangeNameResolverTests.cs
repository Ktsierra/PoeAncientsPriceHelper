using System.Collections.ObjectModel;
using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

public class ExchangeNameResolverTests
{
    private static ExchangeSnapshot Snap(params string[] keys)
    {
        var items = keys.ToDictionary(k => k, k => new ExchangeEntry(k, 1m));
        var byLen = keys.GroupBy(k => k.Length).ToDictionary(g => g.Key, g => g.ToList());
        return new ExchangeSnapshot(new ReadOnlyDictionary<string, ExchangeEntry>(items), byLen, "divine");
    }

    private static ExchangeNameResolver Resolver(params string[] keys) =>
        new(Snap(keys), NameTranslator.Empty);

    [Fact]
    public void Resolve_ExactAfterNormalization()
    {
        var r = Resolver("divine orb", "exalted orb");
        Assert.Equal("divine orb", r.Resolve("Divine Orb"));
        Assert.Equal("exalted orb", r.Resolve("  EXALTED   ORB "));
    }

    // A single-character OCR slip is rescued by the fuzzy step (same 0.84 bar as the remnant matcher).
    [Fact]
    public void Resolve_FuzzyRescuesOcrSlip()
    {
        var r = Resolver("divine orb", "orb of augmentation");
        Assert.Equal("divine orb", r.Resolve("Dlvine Orb"));
        Assert.Equal("orb of augmentation", r.Resolve("Orb of Augmentatlon"));
    }

    // Digits OCR'd for look-alike letters fold back before lookup ("0lvine" → "olvine" → fuzzy hit).
    [Fact]
    public void Resolve_DigitFoldThenFuzzy()
    {
        var r = Resolver("divine orb");
        Assert.Equal("divine orb", r.Resolve("D1vine Orb"));
    }

    [Fact]
    public void Resolve_GarbageAndShortText_ReturnNull()
    {
        var r = Resolver("divine orb");
        Assert.Null(r.Resolve("xyzzy plugh"));
        Assert.Null(r.Resolve("al"));          // below MinExactLength
        Assert.Null(r.Resolve("   "));
        Assert.Null(r.Resolve("I Want"));      // panel chrome must not resolve to an item
    }

    // Section headers / tab-rail words must not resolve — that's what excludes them in the detector.
    [Fact]
    public void Resolve_PanelChromeWords_ReturnNull()
    {
        var r = Resolver("divine orb", "exalted orb", "chaos orb", "vaal orb");
        Assert.Null(r.Resolve("Currency"));
        Assert.Null(r.Resolve("Jewellers' Currency"));
        Assert.Null(r.Resolve("Essences"));
    }
}
