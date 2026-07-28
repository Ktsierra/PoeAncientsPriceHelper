using System.Drawing;

namespace PoeAncientsPriceHelper;

// Pure geometry for laying picker badges out uniformly. Split out of ExchangeOverlayForm so it is
// unit-testable without a window, a screen, or GDI.
//
// Why this exists (first Windows test round, W-6): pills used to derive their font from EACH cell's
// own OCR text height and anchor at `cellText.Right + gap`. Both are wrong for a grid:
//
//   * Wrapped names ("Orb of Augmentation") are merged by the detector with Rectangle.Union, so their
//     bounds are ~2x the height of a single-line cell. Feeding that into a per-cell font bucket gave
//     those cells a visibly larger pill — the "showed in different sizes" report.
//   * Anchoring to the TEXT's right edge puts a short name's pill mid-cell and a long name's pill past
//     the cell entirely, on top of the next column — the "covering other currency" report.
//
// Both are fixed by treating the picker as what it is: a grid. One font for the whole panel, derived
// from the single-line rows only, and pills right-aligned inside their own column so they can never
// cross into the neighbour.
internal static class ExchangeBadgeLayout
{
    // A cell taller than this multiple of the panel's smallest cell is a wrapped two-line name, not a
    // bigger font. 1.5x sits comfortably between one line (1.0x) and two (~2.0x).
    private const double WrappedHeightRatio = 1.5;

    // Column clustering: two cells belong to different columns when their label LEFT edges differ by
    // more than this fraction of the panel width. The picker is a 3-column grid, so real column gaps
    // are ~1/3 of the panel; anything under a tenth is the same column.
    private const double ColumnSplitFraction = 0.10;

    // Clearance between a pill's right edge and the next column's label, as a fraction of the column
    // pitch, clamped so it stays sane from 1080p to 4K.
    private const int GutterDivisor = 20;
    private const int MinGutterPx = 4;
    private const int MaxGutterPx = 24;

    // Uniform pill font height (px) for the whole picker.
    //
    // Uses the MEDIAN of the single-line cells only. Median (not mean) so a stray OCR box that merged
    // two rows can't drag the size; single-line-only so wrapped names — which are legitimately twice
    // as tall — don't inflate every pill on the panel. Returns a clamped px height matching the old
    // per-cell curve, so a correctly-detected single-line panel renders the same size it always did.
    public static int UniformFontPx(IReadOnlyList<Rectangle> cells)
    {
        if (cells.Count == 0) return 13;

        int min = int.MaxValue;
        foreach (var c in cells) if (c.Height > 0 && c.Height < min) min = c.Height;
        if (min == int.MaxValue) return 13;

        var singles = new List<int>(cells.Count);
        foreach (var c in cells)
            if (c.Height > 0 && c.Height <= min * WrappedHeightRatio) singles.Add(c.Height);
        if (singles.Count == 0) singles.Add(min);

        singles.Sort();
        int median = singles[singles.Count / 2];
        return FontPxForHeight(median);
    }

    // The original per-cell curve, kept identical so the main-view pill and any single-line picker
    // keep their existing size. Even px only, so a handful of cached Font objects covers every panel.
    public static int FontPxForHeight(int cellHeightPx) =>
        Math.Clamp((int)(cellHeightPx * 0.62) / 2 * 2, 12, 22);

    // The picker grid's x geometry: each column's label LEFT edge, and the right edge a pill in that
    // column must stop at.
    //
    // Left edges, NOT x-centres. This was the second in-game failure (pills "randomly around the
    // square, not in a fixed position", and wrapped names looking like they had no badge at all).
    // A centre is Left + Width/2, and Width is the OCR'd TEXT width — "Divine Orb" and "Omen of
    // Chaotic Quantity" sit in the same column with centres ~80px apart. That made the derived
    // boundary depend on which names happened to resolve on a given pass, so pills shifted every
    // scan; and a long enough name's centre crossed into the next column's range, which drew its
    // pill over the neighbour. Wrapped two-line cells were worst hit: the detector unions both
    // fragments, and the tier numeral ("II"/"III") on the second line drags Left out and Width up.
    //
    // Every label in a column starts at the same x — right after the icon — whatever its length, so
    // the left edge is the one quantity the game actually holds fixed.
    public static PickerColumns Columns(IReadOnlyList<Rectangle> cells, Rectangle panel)
    {
        if (cells.Count == 0) return new PickerColumns([], []);

        var lefts = new List<int>(cells.Count);
        foreach (var c in cells) lefts.Add(c.Left);
        lefts.Sort();

        int split = Math.Max(1, (int)(panel.Width * ColumnSplitFraction));
        var columnLefts = new List<int>();
        int runStart = 0;
        for (int i = 1; i <= lefts.Count; i++)
        {
            if (i == lefts.Count || lefts[i] - lefts[i - 1] > split)
            {
                // Median of the run, so one wrapped cell's dragged-out left can't move the column.
                columnLefts.Add(lefts[(runStart + i - 1) / 2]);
                runStart = i;
            }
        }

        // Column pitch, used only to give the LAST column the same width as the others. Stretching it
        // to panel.Right (the old behaviour) tied it to the widest text on the panel, so the right
        // column's pills drifted furthest and could land past the grid onto the scrollbar.
        int pitch;
        if (columnLefts.Count >= 2)
        {
            var gaps = new List<int>(columnLefts.Count - 1);
            for (int i = 1; i < columnLefts.Count; i++) gaps.Add(columnLefts[i] - columnLefts[i - 1]);
            gaps.Sort();
            pitch = gaps[gaps.Count / 2];
        }
        else pitch = Math.Max(1, panel.Right - columnLefts[0]);

        int gutter = Math.Clamp(pitch / GutterDivisor, MinGutterPx, MaxGutterPx);
        var rights = new List<int>(columnLefts.Count);
        for (int i = 0; i < columnLefts.Count; i++)
        {
            int nextLeft = i < columnLefts.Count - 1 ? columnLefts[i + 1] : columnLefts[i] + pitch;
            rights.Add(nextLeft - gutter);
        }
        return new PickerColumns(columnLefts, rights);
    }

    // The right edge of the column this cell sits in — the boundary a pill must not cross. Matched on
    // the NEAREST column left rather than a "first edge past me" scan, so a wrapped cell whose union
    // bounds start slightly left of its column still resolves to its own column instead of the
    // previous one.
    public static int RightFor(Rectangle cell, PickerColumns columns, Rectangle panel)
    {
        if (columns.Lefts.Count == 0) return panel.Right;

        int best = 0, bestDist = int.MaxValue;
        for (int i = 0; i < columns.Lefts.Count; i++)
        {
            int dist = Math.Abs(columns.Lefts[i] - cell.Left);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return columns.Rights[best];
    }
}

// Column left edges and the matching pill right-stop, ordered left to right and always the same
// length.
internal sealed record PickerColumns(IReadOnlyList<int> Lefts, IReadOnlyList<int> Rights);
