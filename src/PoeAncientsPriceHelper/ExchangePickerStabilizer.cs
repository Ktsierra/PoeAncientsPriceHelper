using System.Drawing;

namespace PoeAncientsPriceHelper;

// Holds picker cells steady across scan passes so badges stop blinking on and off as OCR reads the
// same grid slightly differently each time. The exchange analogue of RumourStabilizer — but keyed by
// cell POSITION rather than row index, because the picker is a spatial grid, not a short ordered list
// (same reason the price overlay's RowSlot matches on Y with a tolerance).
//
// Reported behaviour that prompted this: "it shows sometimes... off, then on for a few seconds". The
// grid does not change while the picker is open, so a cell that resolved once should keep its badge
// through a pass where OCR clipped its name — but it must not outlive the panel, or a scrolled or
// closed picker would leave badges floating over nothing. Hence the miss budget.
internal sealed class ExchangePickerStabilizer
{
    // Cell centres jitter by a few px between passes as OCR boxes shift; this much movement is still
    // "the same cell". Comfortably under the picker's row pitch, so neighbours can never be confused.
    private const int MatchTolerancePx = 12;

    // Passes a remembered cell survives without being re-read before it is dropped. Three passes at
    // the 900 ms default is ~2.7 s of cover — enough to ride out OCR jitter, short enough that a
    // closed or scrolled panel clears quickly.
    private const int MissesBeforeDrop = 3;

    // A panel that jumps further than this is a different view (scrolled, reopened, resized), not
    // jitter — start over rather than dragging stale cells onto it.
    private const int PanelShiftResetPx = 24;

    private sealed class Slot
    {
        public ExchangeCell Cell = null!;
        public int Misses;
    }

    private readonly List<Slot> _slots = new();
    private ExchangeSide? _side;
    private Rectangle _panel;

    public void Reset()
    {
        _slots.Clear();
        _side = null;
        _panel = Rectangle.Empty;
    }

    // Merge one raw picker read and return the stabilised cell set. Cells seen this pass are refreshed;
    // cells briefly missed are re-emitted from memory until their miss budget runs out.
    public IReadOnlyList<ExchangeCell> Stabilize(ExchangeSide side, Rectangle panelBounds,
        IReadOnlyList<ExchangeCell> read)
    {
        if (_side != side || PanelMoved(panelBounds))
        {
            _slots.Clear();
            _side = side;
        }
        _panel = panelBounds;

        foreach (var slot in _slots) slot.Misses++;

        foreach (var cell in read)
        {
            var slot = FindSlot(cell.Bounds);
            if (slot is null) _slots.Add(new Slot { Cell = cell, Misses = 0 });
            else { slot.Cell = cell; slot.Misses = 0; }
        }

        _slots.RemoveAll(s => s.Misses > MissesBeforeDrop);

        var result = new List<ExchangeCell>(_slots.Count);
        foreach (var slot in _slots) result.Add(slot.Cell);
        result.Sort((a, b) => a.Bounds.Top != b.Bounds.Top
            ? a.Bounds.Top.CompareTo(b.Bounds.Top)
            : a.Bounds.Left.CompareTo(b.Bounds.Left));
        return result;
    }

    private bool PanelMoved(Rectangle panel) =>
        _panel != Rectangle.Empty &&
        (Math.Abs(_panel.Left - panel.Left) > PanelShiftResetPx ||
         Math.Abs(_panel.Top - panel.Top) > PanelShiftResetPx);

    // The remembered slot whose centre is within tolerance of this one, or null for a new cell.
    private Slot? FindSlot(Rectangle bounds)
    {
        int cx = bounds.Left + bounds.Width / 2, cy = bounds.Top + bounds.Height / 2;
        foreach (var slot in _slots)
        {
            var b = slot.Cell.Bounds;
            if (Math.Abs(b.Left + b.Width / 2 - cx) <= MatchTolerancePx &&
                Math.Abs(b.Top + b.Height / 2 - cy) <= MatchTolerancePx)
                return slot;
        }
        return null;
    }
}
