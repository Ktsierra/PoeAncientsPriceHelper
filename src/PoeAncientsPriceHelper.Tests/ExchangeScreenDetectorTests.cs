using System.Drawing;
using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

public class ExchangeScreenDetectorTests
{
    private static OcrTextLine Line(string text, int x, int y, int w = 160, int h = 22) =>
        new(text, new Rectangle(x, y, w, h));

    // The detector never resolves names itself — tests inject a fixed vocabulary.
    private static readonly HashSet<string> Known =
    [
        "scroll of wisdom", "regal orb", "exalted orb", "chaos orb", "vaal orb",
        "orb of chance", "orb of augmentation", "divine orb",
    ];

    private static string? Resolve(string raw)
    {
        var n = NameNormalizer.Normalize(raw);
        return Known.Contains(n) ? n : null;
    }

    // Modelled on the real main exchange view (gamerant/maxroll reference screenshots, 2200×1100):
    // title top-centre, MARKET RATIO + value beneath, side labels with the selected names below them.
    private static List<OcrTextLine> MainViewLines(bool withWant = true, bool withHave = true)
    {
        var lines = new List<OcrTextLine>
        {
            Line("CURRENCY EXCHANGE", 940, 130, 320, 30),
            Line("MARKET RATIO", 1020, 195, 170, 22),
            Line("1 : 17", 1075, 220, 60, 20),
            Line("I WANT", 660, 228, 80, 20),
            Line("I HAVE", 1370, 228, 80, 20),
            Line("PLACE ORDER", 1000, 375, 160, 22),
            Line("1,000", 1035, 340, 60, 18),
        };
        if (withWant) lines.Add(Line("Orb of Chance", 680, 275, 150, 24));
        if (withHave) lines.Add(Line("Vaal Orb", 1390, 275, 100, 24));
        return lines;
    }

    [Fact]
    public void Detect_MainView_BothSidesSelected()
    {
        var det = ExchangeScreenDetector.Detect(MainViewLines(), Resolve);
        Assert.NotNull(det.Main);
        Assert.Null(det.Picker);
        Assert.Equal("orb of chance", det.Main!.WantKey);
        Assert.Equal("vaal orb", det.Main.HaveKey);
        Assert.NotNull(det.Main.MarketRatioBounds);
    }

    [Fact]
    public void Detect_MainView_EmptySideReadsAsNull()
    {
        var det = ExchangeScreenDetector.Detect(MainViewLines(withHave: false), Resolve);
        Assert.NotNull(det.Main);
        Assert.Equal("orb of chance", det.Main!.WantKey);
        Assert.Null(det.Main.HaveKey);

        det = ExchangeScreenDetector.Detect(MainViewLines(withWant: false, withHave: false), Resolve);
        Assert.NotNull(det.Main);
        Assert.Null(det.Main!.WantKey);
        Assert.Null(det.Main.HaveKey);
    }

    // The stylised title survives an OCR slip (fuzzy signature, like the rumour detector's headers).
    [Fact]
    public void Detect_MainView_GarbledTitleStillMatches()
    {
        var lines = MainViewLines();
        lines[0] = Line("CURRENCV EXCHANGE", 940, 130, 320, 30);
        var det = ExchangeScreenDetector.Detect(lines, Resolve);
        Assert.NotNull(det.Main);
    }

    // Title alone is not enough — a corroborating signature is required (multi-signature confirm).
    [Fact]
    public void Detect_TitleWithoutCorroboration_IsNone()
    {
        List<OcrTextLine> lines =
        [
            Line("CURRENCY EXCHANGE", 940, 130, 320, 30),
            Line("Some flavour text", 900, 300),
        ];
        Assert.True(ExchangeScreenDetector.Detect(lines, Resolve).IsNone);
    }

    // The quantity boxes ("1", "17") and the ratio value line sit between the slots — digits never
    // resolve, so they can't be mistaken for a selected currency.
    [Fact]
    public void Detect_MainView_NumbersDontBecomeSelections()
    {
        var lines = MainViewLines(withWant: false, withHave: false);
        lines.Add(Line("17", 1240, 275, 40, 20));
        lines.Add(Line("1", 890, 275, 20, 20));
        var det = ExchangeScreenDetector.Detect(lines, Resolve);
        Assert.NotNull(det.Main);
        Assert.Null(det.Main!.WantKey);
        Assert.Null(det.Main.HaveKey);
    }

    // A currency name far below the slot band (e.g. in the order list) must not be picked up.
    [Fact]
    public void Detect_MainView_IgnoresNamesOutsideSlotBand()
    {
        var lines = MainViewLines(withWant: false, withHave: true);
        lines.Add(Line("Divine Orb", 700, 600, 130, 24));   // order-book row, way below the slots
        var det = ExchangeScreenDetector.Detect(lines, Resolve);
        Assert.NotNull(det.Main);
        Assert.Null(det.Main!.WantKey);
        Assert.Equal("vaal orb", det.Main.HaveKey);
    }

    [Fact]
    public void Detect_RandomFrame_IsNone()
    {
        List<OcrTextLine> lines =
        [
            Line("Hunting Grounds", 1700, 400),
            Line("WORLD", 1100, 8, 90, 22),
        ];
        Assert.True(ExchangeScreenDetector.Detect(lines, Resolve).IsNone);
        Assert.True(ExchangeScreenDetector.Detect([], Resolve).IsNone);
    }
}
