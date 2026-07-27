using System.Drawing;
using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

// Geometry for the picker badge layout (W-6). Bounds here are modelled on the real 3-column picker
// read off a 1188x1030 screenshot: single-line labels ~22px tall, wrapped two-line labels ~45px,
// columns centred near x=435 / 733 / 1029.
public class ExchangeBadgeLayoutTests
{
    private static readonly Rectangle Panel = new(290, 60, 900, 960);

    private static Rectangle Cell(int left, int top, int width, int height) => new(left, top, width, height);

    // A realistic mixed panel: mostly single-line names, a few wrapped ones.
    private static List<Rectangle> MixedPanel() =>
    [
        Cell(345, 125, 130, 22),   // Exalted Orb
        Cell(645, 125, 110, 22),   // Chaos Orb
        Cell(940, 125, 200, 22),   // Armourer's Scrap
        Cell(345, 240, 175, 22),   // Orb of Alchemy
        Cell(645, 240, 165, 22),   // Orb of Chance
        Cell(940, 235, 150, 45),   // Blacksmith's Whetstone  <- wrapped, ~2x tall
        Cell(345, 300, 190, 45),   // Lesser Jeweller's Orb   <- wrapped
        Cell(645, 310, 160, 22),   // Artificer's Orb
        Cell(940, 310, 110, 22),   // Iron Rune
    ];

    [Fact]
    public void UniformFontPx_IgnoresWrappedCells()
    {
        // The wrapped 45px cells must not drag the panel font up: the size comes from the 22px rows.
        int px = ExchangeBadgeLayout.UniformFontPx(MixedPanel());
        Assert.Equal(ExchangeBadgeLayout.FontPxForHeight(22), px);
    }

    [Fact]
    public void UniformFontPx_IsIdenticalForEveryCellOnThePanel()
    {
        // The actual regression: one size for the whole picker, whatever the mix of cells.
        var cells = MixedPanel();
        int px = ExchangeBadgeLayout.UniformFontPx(cells);
        foreach (var _ in cells)
            Assert.Equal(px, ExchangeBadgeLayout.UniformFontPx(cells));
    }

    [Fact]
    public void UniformFontPx_UsesMedianSoAStrayMergedRowCannotSkewIt()
    {
        // One badly-merged OCR box among clean rows must not change the panel's size.
        var clean = new List<Rectangle>
        {
            Cell(345, 125, 130, 22), Cell(645, 125, 110, 22), Cell(940, 125, 200, 22),
            Cell(345, 180, 130, 22), Cell(645, 180, 110, 22),
        };
        int before = ExchangeBadgeLayout.UniformFontPx(clean);
        clean.Add(Cell(345, 240, 130, 120));   // garbage box spanning several rows
        Assert.Equal(before, ExchangeBadgeLayout.UniformFontPx(clean));
    }

    [Fact]
    public void UniformFontPx_ScalesWithResolution()
    {
        // A 4K panel has taller cells and must get a bigger pill than a 1080p one.
        var small = new List<Rectangle> { Cell(345, 125, 130, 18), Cell(645, 125, 110, 18) };
        var large = new List<Rectangle> { Cell(345, 125, 260, 40), Cell(645, 125, 220, 40) };
        Assert.True(ExchangeBadgeLayout.UniformFontPx(large) > ExchangeBadgeLayout.UniformFontPx(small));
    }

    [Fact]
    public void UniformFontPx_EmptyIsSafe()
    {
        Assert.Equal(13, ExchangeBadgeLayout.UniformFontPx([]));
    }

    [Fact]
    public void ColumnRightEdges_FindsThreeColumns()
    {
        var edges = ExchangeBadgeLayout.ColumnRightEdges(MixedPanel(), Panel);
        Assert.Equal(3, edges.Count);
    }

    [Fact]
    public void ColumnRightEdges_LastColumnReachesThePanelEdge()
    {
        var edges = ExchangeBadgeLayout.ColumnRightEdges(MixedPanel(), Panel);
        Assert.Equal(Panel.Right, edges[^1]);
    }

    [Fact]
    public void ColumnRightEdges_AreOrderedAndSeparated()
    {
        var edges = ExchangeBadgeLayout.ColumnRightEdges(MixedPanel(), Panel);
        for (int i = 1; i < edges.Count; i++)
            Assert.True(edges[i] > edges[i - 1], "column edges must increase left to right");
    }

    [Fact]
    public void ColumnRightFor_KeepsAPillOutOfTheNextColumn()
    {
        // The reported bug: a long name's pill landed on the next column's label. The boundary for a
        // left-column cell must sit left of the middle column's leftmost text.
        var cells = MixedPanel();
        var edges = ExchangeBadgeLayout.ColumnRightEdges(cells, Panel);
        var longLeftCell = Cell(345, 300, 190, 45);          // Lesser Jeweller's Orb
        int boundary = ExchangeBadgeLayout.ColumnRightFor(longLeftCell, edges, Panel);
        int middleColumnLeftmostText = 645;
        Assert.True(boundary <= middleColumnLeftmostText,
            $"left-column boundary {boundary} must not reach middle-column text at {middleColumnLeftmostText}");
    }

    [Fact]
    public void ColumnRightFor_AllCellsInAColumnShareOneBoundary()
    {
        // Uniformity: every pill in a column right-aligns to the same x, whatever the name length.
        var cells = MixedPanel();
        var edges = ExchangeBadgeLayout.ColumnRightEdges(cells, Panel);
        int shortName = ExchangeBadgeLayout.ColumnRightFor(Cell(645, 125, 110, 22), edges, Panel);
        int longName = ExchangeBadgeLayout.ColumnRightFor(Cell(645, 240, 165, 22), edges, Panel);
        Assert.Equal(shortName, longName);
    }

    [Fact]
    public void ColumnRightFor_SingleColumnPanelUsesPanelEdge()
    {
        var single = new List<Rectangle> { Cell(645, 125, 110, 22), Cell(645, 180, 130, 22) };
        var edges = ExchangeBadgeLayout.ColumnRightEdges(single, Panel);
        Assert.Single(edges);
        Assert.Equal(Panel.Right, ExchangeBadgeLayout.ColumnRightFor(single[0], edges, Panel));
    }

    [Fact]
    public void ColumnRightEdges_EmptyIsSafe()
    {
        Assert.Empty(ExchangeBadgeLayout.ColumnRightEdges([], Panel));
    }
}
