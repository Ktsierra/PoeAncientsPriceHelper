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

    // Column clustering: two cells belong to different columns when their x-centres differ by more
    // than this fraction of the panel width. The picker is a 3-column grid, so real column gaps are
    // ~1/3 of the panel; anything under a tenth is the same column with different-length names.
    private const double ColumnSplitFraction = 0.10;

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

    // Right-edge x for each column of the picker, ordered left to right.
    //
    // Derived from the cells' x-centres rather than their text extents: text width varies wildly per
    // name, but the centre of a grid cell's label is stable. A column's right edge is the midpoint
    // between its centre and the next column's centre (so a pill can fill its own cell but never
    // reach the neighbour's text); the last column extends to the panel's right edge.
    public static IReadOnlyList<int> ColumnRightEdges(IReadOnlyList<Rectangle> cells, Rectangle panel)
    {
        if (cells.Count == 0) return [];

        var centres = new List<int>(cells.Count);
        foreach (var c in cells) centres.Add(c.Left + c.Width / 2);
        centres.Sort();

        int split = Math.Max(1, (int)(panel.Width * ColumnSplitFraction));
        var columnCentres = new List<int>();
        int runStart = 0;
        for (int i = 1; i <= centres.Count; i++)
        {
            if (i == centres.Count || centres[i] - centres[i - 1] > split)
            {
                columnCentres.Add((centres[runStart] + centres[i - 1]) / 2);
                runStart = i;
            }
        }

        var edges = new List<int>(columnCentres.Count);
        for (int i = 0; i < columnCentres.Count; i++)
            edges.Add(i == columnCentres.Count - 1
                ? panel.Right
                : (columnCentres[i] + columnCentres[i + 1]) / 2);
        return edges;
    }

    // The right edge of the column this cell sits in — the boundary a pill must not cross.
    public static int ColumnRightFor(Rectangle cell, IReadOnlyList<int> columnRightEdges, Rectangle panel)
    {
        int centre = cell.Left + cell.Width / 2;
        foreach (int edge in columnRightEdges)
            if (centre <= edge) return edge;
        return columnRightEdges.Count > 0 ? columnRightEdges[^1] : panel.Right;
    }
}
