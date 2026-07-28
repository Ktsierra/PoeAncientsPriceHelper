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

    // The live failure: the stylised title did not OCR at all, and requiring it sent every main-view
    // frame down the picker branch, where "both side headers" failed the exactly-one test and came
    // back None. A whole session never once classified MAIN, so the pair was never remembered.
    [Fact]
    public void Detect_MainView_SurvivesAMissingTitle()
    {
        var lines = MainViewLines();
        lines.RemoveAt(0);   // title line absent entirely

        var det = ExchangeScreenDetector.Detect(lines, Resolve);

        Assert.NotNull(det.Main);
        Assert.Null(det.Picker);
        Assert.Equal("orb of chance", det.Main!.WantKey);
        Assert.Equal("vaal orb", det.Main.HaveKey);
    }

    // Promoting on the two side headers must not lower the bar: without a corroborating "Market
    // Ratio" / "Place Order" line, both headers alone still decide nothing.
    [Fact]
    public void Detect_BothHeadersWithoutTitleOrCorroboration_IsNone()
    {
        List<OcrTextLine> lines =
        [
            Line("I WANT", 660, 228, 80, 20),
            Line("I HAVE", 1370, 228, 80, 20),
            Line("Orb of Chance", 680, 275, 150, 24),
        ];
        Assert.True(ExchangeScreenDetector.Detect(lines, Resolve).IsNone);
    }

    // A wrapped name's badge must anchor to the LOWER line, not the union of both. Centring on the
    // union puts the pill in the gap between the two lines — the "floating in the middle of the
    // square" report — and the union also carries the tier numeral's left overhang.
    [Fact]
    public void Detect_Picker_WrappedCellAnchorsToItsLowerLine()
    {
        List<OcrTextLine> lines =
        [
            Line("I WANT", 660, 100, 80, 20),
            Line("Exalted Orb", 345, 160, 130, 22),
            Line("Chaos Orb", 645, 160, 110, 22),
            Line("Orb of", 940, 160, 70, 22),
            Line("Augmentation", 930, 184, 140, 22),
        ];

        var det = ExchangeScreenDetector.Detect(lines, Resolve);

        Assert.NotNull(det.Picker);
        var wrapped = det.Picker!.Cells.Single(c => c.Key == "orb of augmentation");
        // Bounds span both lines; the anchor is just the lower one.
        Assert.Equal(160, wrapped.Bounds.Top);
        Assert.Equal(206, wrapped.Bounds.Bottom);
        Assert.Equal(184, wrapped.AnchorBounds.Top);
        Assert.Equal(22, wrapped.AnchorBounds.Height);
    }

    // Single-line cells have no separate anchor — Bounds is already the line.
    [Fact]
    public void Detect_Picker_SingleLineCellAnchorsToItself()
    {
        var det = ExchangeScreenDetector.Detect(PickerLines(), Resolve);
        Assert.NotNull(det.Picker);
        foreach (var cell in det.Picker!.Cells.Where(c => c.Anchor is null))
            Assert.Equal(cell.Bounds, cell.AnchorBounds);
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

    // Modelled on the real "I Want" picker screenshot (2200×1100): big side header top-centre,
    // category tab rail on the left, section headers between rows, 3 columns of icon+name cells.
    private static List<OcrTextLine> PickerLines(string header = "I WANT") =>
    [
        Line(header, 1050, 25, 110, 28),
        Line("All", 535, 95, 40, 20),                       // tab rail — never resolves
        Line("Currency", 535, 148, 90, 20),
        Line("Essences", 535, 415, 90, 20),
        Line("CURRENCY", 1120, 88, 110, 18),                // section header — never resolves
        Line("Scroll of Wisdom", 860, 135, 170, 22),
        Line("Orb of", 1155, 125, 65, 20),                  // wrapped two-line name…
        Line("Augmentation", 1155, 148, 130, 20),           // …resolves only concatenated
        Line("Regal Orb", 860, 192, 100, 22),
        Line("Exalted Orb", 1155, 192, 120, 22),
        Line("Chaos Orb", 1440, 192, 105, 22),
        Line("Divine Orb", 1440, 250, 110, 22),
    ];

    [Fact]
    public void Detect_Picker_SideCellsAndOrder()
    {
        var det = ExchangeScreenDetector.Detect(PickerLines(), Resolve);
        Assert.Null(det.Main);
        Assert.NotNull(det.Picker);
        Assert.Equal(ExchangeSide.Want, det.Picker!.Side);
        // Sorted by Bounds.Top then Left: the merged two-line cell's union top (125) precedes
        // "Scroll of Wisdom" (135).
        Assert.Equal(
            new[] { "orb of augmentation", "scroll of wisdom", "regal orb", "exalted orb", "chaos orb", "divine orb" },
            det.Picker.Cells.Select(c => c.Key).ToArray());
    }

    [Fact]
    public void Detect_Picker_HaveHeader()
    {
        var det = ExchangeScreenDetector.Detect(PickerLines("I HAVE"), Resolve);
        Assert.Equal(ExchangeSide.Have, det.Picker!.Side);
    }

    // The merged cell's bounds must cover BOTH fragments so the badge anchors past the longer line.
    [Fact]
    public void Detect_Picker_WrappedNameBoundsAreUnion()
    {
        var det = ExchangeScreenDetector.Detect(PickerLines(), Resolve);
        var merged = det.Picker!.Cells.Single(c => c.Key == "orb of augmentation");
        Assert.Equal(Rectangle.Union(new Rectangle(1155, 125, 65, 20), new Rectangle(1155, 148, 130, 20)),
            merged.Bounds);
    }

    // Chrome (tabs, section headers) must not appear as cells, and a picker needs ≥3 resolved cells.
    [Fact]
    public void Detect_Picker_ChromeExcluded_AndMinCells()
    {
        var det = ExchangeScreenDetector.Detect(PickerLines(), Resolve);
        Assert.DoesNotContain(det.Picker!.Cells, c => c.Key.Contains("currency"));

        List<OcrTextLine> sparse =
        [
            Line("I WANT", 1050, 25, 110, 28),
            Line("Divine Orb", 1155, 192, 110, 22),
            Line("Chaos Orb", 1440, 192, 105, 22),
        ];
        Assert.True(ExchangeScreenDetector.Detect(sparse, Resolve).IsNone);
    }

    // A frame with BOTH side labels but no title (main view whose title failed OCR) stays None —
    // never misclassified as a picker.
    [Fact]
    public void Detect_BothLabelsWithoutTitle_IsNone()
    {
        List<OcrTextLine> lines =
        [
            Line("I WANT", 660, 228, 80, 20),
            Line("I HAVE", 1370, 228, 80, 20),
            Line("Orb of Chance", 680, 275, 150, 24),
            Line("Vaal Orb", 1390, 275, 100, 24),
            Line("Divine Orb", 700, 320, 130, 24),
        ];
        Assert.True(ExchangeScreenDetector.Detect(lines, Resolve).IsNone);
    }

    [Theory]
    [InlineData(0, 0, 100, 20, 0, 22, 100, 20, true)]     // stacked, full overlap
    [InlineData(0, 0, 60, 20, 5, 23, 120, 20, true)]      // stacked, partial overlap (wrapped name)
    [InlineData(0, 0, 100, 20, 300, 22, 100, 20, false)]  // same rows, different column
    [InlineData(0, 0, 100, 20, 0, 80, 100, 20, false)]    // too far apart vertically
    public void AreWrappedPair_Geometry(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh, bool expected)
    {
        Assert.Equal(expected, ExchangeScreenDetector.AreWrappedPair(
            new Rectangle(ax, ay, aw, ah), new Rectangle(bx, by, bw, bh)));
    }
}
