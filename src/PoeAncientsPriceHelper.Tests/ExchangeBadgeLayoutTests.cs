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
    public void Columns_FindsThreeColumns()
    {
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        Assert.Equal(3, columns.Lefts.Count);
        Assert.Equal(3, columns.Rights.Count);
    }

    [Fact]
    public void Columns_AreOrderedAndSeparated()
    {
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        for (int i = 1; i < columns.Rights.Count; i++)
            Assert.True(columns.Rights[i] > columns.Rights[i - 1], "column edges must increase left to right");
    }

    [Fact]
    public void RightFor_KeepsAPillOutOfTheNextColumn()
    {
        // A long name's pill must not land on the next column's label.
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        var longLeftCell = Cell(345, 300, 190, 45);          // Lesser Jeweller's Orb
        int boundary = ExchangeBadgeLayout.RightFor(longLeftCell, columns, Panel);
        int middleColumnLeftmostText = 645;
        Assert.True(boundary < middleColumnLeftmostText,
            $"left-column boundary {boundary} must not reach middle-column text at {middleColumnLeftmostText}");
    }

    [Fact]
    public void RightFor_AllCellsInAColumnShareOneBoundary()
    {
        // Uniformity: every pill in a column right-aligns to the same x, whatever the name length.
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        int shortName = ExchangeBadgeLayout.RightFor(Cell(645, 125, 110, 22), columns, Panel);
        int longName = ExchangeBadgeLayout.RightFor(Cell(645, 240, 165, 22), columns, Panel);
        Assert.Equal(shortName, longName);
    }

    // The in-game regression: the boundary used to be derived from x-CENTRES, so it moved whenever a
    // different mix of name lengths resolved. Reading the same grid with wider text must not shift a
    // single pill.
    [Fact]
    public void Columns_DoNotMoveWhenNameLengthsChange()
    {
        var narrow = new List<Rectangle>
        {
            Cell(345, 125, 90, 22), Cell(645, 125, 90, 22), Cell(940, 125, 90, 22),
        };
        var wide = new List<Rectangle>
        {
            Cell(345, 125, 260, 22), Cell(645, 125, 250, 22), Cell(940, 125, 240, 22),
        };

        var a = ExchangeBadgeLayout.Columns(narrow, Panel);
        var b = ExchangeBadgeLayout.Columns(wide, Panel);

        Assert.Equal(a.Rights, b.Rights);
    }

    // Wrapped two-line cells are unioned by the detector, and the tier numeral ("II"/"III") on the
    // second line starts left of the name — dragging the union's Left out. Nearest-column matching
    // must still place it in its own column, not the one before.
    [Fact]
    public void RightFor_WrappedCellDraggedLeftStaysInItsOwnColumn()
    {
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        int normal = ExchangeBadgeLayout.RightFor(Cell(645, 125, 110, 22), columns, Panel);
        var draggedLeft = Cell(645 - 18, 235, 210, 45);   // "Greater Orb of / II Augmentation"
        Assert.Equal(normal, ExchangeBadgeLayout.RightFor(draggedLeft, columns, Panel));
    }

    // The last column used to stretch to panel.Right, which is the union of the cells' TEXT bounds —
    // i.e. wherever the widest name on the panel happened to end. That tied the right column's pills
    // to name lengths the same way the centre-based split did. It should simply be one pitch wide,
    // like every other column. (It may legitimately sit past panel.Right: the game's cell extends
    // beyond its label, and PaintPill clamps to the screen.)
    [Fact]
    public void Columns_LastColumnIsOnePitchWideLikeTheOthers()
    {
        var columns = ExchangeBadgeLayout.Columns(MixedPanel(), Panel);
        int lastWidth = columns.Rights[^1] - columns.Lefts[^1];
        int firstWidth = columns.Rights[0] - columns.Lefts[0];
        Assert.InRange(lastWidth, firstWidth - 4, firstWidth + 4);
    }

    // ...and it must not depend on the widest name, which is what the old panel.Right did.
    [Fact]
    public void Columns_LastColumnIgnoresTheWidestName()
    {
        var cells = MixedPanel();
        var withWideTail = new List<Rectangle>(cells) { Cell(940, 400, 240, 22) };
        Assert.Equal(
            ExchangeBadgeLayout.Columns(cells, Panel).Rights[^1],
            ExchangeBadgeLayout.Columns(withWideTail, Panel).Rights[^1]);
    }

    [Fact]
    public void RightFor_SingleColumnPanelUsesPanelEdge()
    {
        var single = new List<Rectangle> { Cell(645, 125, 110, 22), Cell(645, 180, 130, 22) };
        var columns = ExchangeBadgeLayout.Columns(single, Panel);
        Assert.Single(columns.Lefts);
        Assert.True(ExchangeBadgeLayout.RightFor(single[0], columns, Panel) <= Panel.Right);
    }

    [Fact]
    public void Columns_EmptyIsSafe()
    {
        var columns = ExchangeBadgeLayout.Columns([], Panel);
        Assert.Empty(columns.Lefts);
        Assert.Empty(columns.Rights);
        Assert.Equal(Panel.Right, ExchangeBadgeLayout.RightFor(Cell(1, 1, 1, 1), columns, Panel));
    }
}
