using System.Drawing;
using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

// Cell stability across scan passes. Reported symptom: a badge "shows sometimes... off, then on for a
// few seconds" as OCR reads the same static grid differently pass to pass.
public class ExchangePickerStabilizerTests
{
    private static readonly Rectangle Panel = new(290, 60, 900, 960);

    private static ExchangeCell C(string key, int left, int top, int w = 130, int h = 22) =>
        new(key, new Rectangle(left, top, w, h));

    private static List<ExchangeCell> FullRead() =>
    [
        C("exalted orb", 345, 125),
        C("chaos orb", 645, 125),
        C("divine orb", 940, 125),
    ];

    [Fact]
    public void FirstReadPassesThrough()
    {
        var s = new ExchangePickerStabilizer();
        Assert.Equal(3, s.Stabilize(ExchangeSide.Want, Panel, FullRead()).Count);
    }

    [Fact]
    public void CellMissedForOnePassIsStillShown()
    {
        // The actual bug: Exalted resolves, then a pass clips it, and the badge vanished.
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());

        var dropped = new List<ExchangeCell> { C("chaos orb", 645, 125), C("divine orb", 940, 125) };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, dropped);

        Assert.Equal(3, shown.Count);
        Assert.Contains(shown, c => c.Key == "exalted orb");
    }

    [Fact]
    public void CellMissedForTooLongIsDropped()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        var without = new List<ExchangeCell> { C("chaos orb", 645, 125), C("divine orb", 940, 125) };

        IReadOnlyList<ExchangeCell> shown = [];
        for (int i = 0; i < 5; i++) shown = s.Stabilize(ExchangeSide.Want, Panel, without);

        Assert.Equal(2, shown.Count);
        Assert.DoesNotContain(shown, c => c.Key == "exalted orb");
    }

    [Fact]
    public void SmallJitterIsTheSameCell()
    {
        // OCR boxes shift a few px between passes; that must not create duplicate slots.
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        var jittered = new List<ExchangeCell>
        {
            C("exalted orb", 348, 127), C("chaos orb", 643, 124), C("divine orb", 941, 126),
        };
        Assert.Equal(3, s.Stabilize(ExchangeSide.Want, Panel, jittered).Count);
    }

    [Fact]
    public void ARefreshedCellKeepsItsLatestBounds()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        var moved = new List<ExchangeCell> { C("exalted orb", 349, 128) };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, moved);
        var cell = shown.First(c => c.Key == "exalted orb");
        Assert.Equal(349, cell.Bounds.Left);
    }

    [Fact]
    public void SwitchingSideStartsFresh()
    {
        // I Want -> I Have is a different panel; stale cells must not carry over.
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        var shown = s.Stabilize(ExchangeSide.Have, Panel, [C("regal orb", 345, 125)]);
        Assert.Single(shown);
        Assert.Equal("regal orb", shown[0].Key);
    }

    [Fact]
    public void PanelMovingFarStartsFresh()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        var moved = Panel with { X = Panel.X + 200 };
        var shown = s.Stabilize(ExchangeSide.Want, moved, [C("regal orb", 545, 125)]);
        Assert.Single(shown);
    }

    // Scrolling the picker moves the CELLS while the panel stays put, so the panel-shift reset never
    // fires. In a live session that produced 28 cells read against 107 badges shown — the same
    // currencies re-badged at every stale offset they had passed through.
    [Fact]
    public void ScrollingDoesNotDuplicateBadges()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());

        var scrolled = FullRead().Select(c => c with { Bounds = c.Bounds with { Y = c.Bounds.Y - 60 } }).ToList();
        var shown = s.Stabilize(ExchangeSide.Want, Panel, scrolled);

        Assert.Equal(3, shown.Count);
        Assert.All(shown, c => Assert.Equal(65, c.Bounds.Y));
    }

    // The ghosts that mattered most were cells the scrolled pass did NOT re-read: nothing refreshed
    // them, so the miss budget kept drawing them at a position the grid had already left.
    [Fact]
    public void ScrollDropsGhostsThatWereNotReRead()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());

        // Two of the three reappear 60px higher; "divine orb" scrolled out of the read entirely.
        var scrolled = new List<ExchangeCell> { C("exalted orb", 345, 65), C("chaos orb", 645, 65) };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, scrolled);

        Assert.Equal(2, shown.Count);
        Assert.DoesNotContain(shown, c => c.Key == "divine orb");
    }

    // A key read at a new position is the same grid cell that moved, never a second one — even when
    // the shift is too small or too partial to be called a scroll.
    [Fact]
    public void SameKeyAtANewPositionReplacesTheOldSlot()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());

        // Only Exalted moves, and far enough to open a new slot; the others hold station, so the
        // median vote says "no scroll" and the duplicate drop is what has to catch this.
        var read = new List<ExchangeCell>
        {
            C("exalted orb", 345, 190), C("chaos orb", 645, 125), C("divine orb", 940, 125),
        };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, read);

        Assert.Equal(3, shown.Count);
        Assert.Equal(190, shown.Single(c => c.Key == "exalted orb").Bounds.Y);
    }

    // The anti-blink guarantee must survive the duplicate drop: a cell absent from the read is not a
    // stale duplicate, so it still rides out its miss budget.
    [Fact]
    public void ScrollFixDoesNotBreakMissRetention()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());

        var clipped = new List<ExchangeCell> { C("chaos orb", 645, 125), C("divine orb", 940, 125) };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, clipped);

        Assert.Equal(3, shown.Count);
        Assert.Contains(shown, c => c.Key == "exalted orb");
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var s = new ExchangePickerStabilizer();
        s.Stabilize(ExchangeSide.Want, Panel, FullRead());
        s.Reset();
        Assert.Single(s.Stabilize(ExchangeSide.Want, Panel, [C("regal orb", 345, 125)]));
    }

    [Fact]
    public void OutputIsOrderedTopToBottomThenLeftToRight()
    {
        var s = new ExchangePickerStabilizer();
        var read = new List<ExchangeCell>
        {
            C("divine orb", 940, 300), C("exalted orb", 345, 125), C("chaos orb", 645, 125),
        };
        var shown = s.Stabilize(ExchangeSide.Want, Panel, read);
        Assert.Equal(["exalted orb", "chaos orb", "divine orb"], shown.Select(c => c.Key));
    }
}
