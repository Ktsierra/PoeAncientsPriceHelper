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

    // A panel that jumps further than this is a different view (reopened, resized), not jitter —
    // start over rather than dragging stale cells onto it. NOTE this catches the panel MOVING; it
    // does not catch the list scrolling inside a stationary panel. See ScrollDetected.
    private const int PanelShiftResetPx = 24;

    // How many remembered cells must agree on a vertical shift before it counts as a scroll. Two is
    // enough to rule out a single mis-placed OCR box, and low enough to fire on the first scrolled
    // pass — waiting longer would just let ghosts render.
    private const int MinScrollVotes = 2;

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

        // The picker scrolls its CONTENT while the panel itself stays put, so PanelMoved above never
        // fires on a scroll. Without this the retention above turns into ghosting: every cell that
        // scrolled to a new Y opened a fresh slot while its old slot lived on for the miss budget,
        // and a live session logged 28 cells read against 107 badges shown — the same currency drawn
        // at three stale offsets, some of it landing mid-cell over the art. If the cells we remember
        // are being read at a consistently different Y, the list moved under us and EVERY remembered
        // position is stale, including the ones this pass happened to miss.
        if (ScrollDetected(read)) _slots.Clear();

        foreach (var slot in _slots) slot.Misses++;

        var refreshed = new HashSet<Slot>();
        foreach (var cell in read)
        {
            var slot = FindSlot(cell.Bounds);
            if (slot is null) { slot = new Slot { Cell = cell }; _slots.Add(slot); }
            else { slot.Cell = cell; slot.Misses = 0; }
            refreshed.Add(slot);
        }

        // A currency occupies exactly one cell of the grid, so the same key remembered at a DIFFERENT
        // position is always a leftover — never a second real cell. Dropping it is what actually
        // guarantees no duplicate badge survives, including a scroll too small or too partial for
        // ScrollDetected to call. Cells genuinely missed this pass keep their badge: their key isn't
        // in this read, so they fall through untouched and ride out the miss budget as before.
        var readKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in read) readKeys.Add(cell.Key);
        _slots.RemoveAll(s => !refreshed.Contains(s) && readKeys.Contains(s.Cell.Key));

        _slots.RemoveAll(s => s.Misses > MissesBeforeDrop);

        var result = new List<ExchangeCell>(_slots.Count);
        foreach (var slot in _slots) result.Add(slot.Cell);
        result.Sort((a, b) => a.Bounds.Top != b.Bounds.Top
            ? a.Bounds.Top.CompareTo(b.Bounds.Top)
            : a.Bounds.Left.CompareTo(b.Bounds.Left));
        return result;
    }

    // True when the remembered cells that reappear in this read have shifted vertically by a
    // consistent amount — the signature of the list scrolling inside a stationary panel. Matched by
    // KEY (not position, which is the thing that moved), and decided on the MEDIAN so one stray box
    // can't outvote the grid. A scroll of less than the match tolerance is indistinguishable from
    // jitter and is deliberately left to the duplicate drop instead.
    private bool ScrollDetected(IReadOnlyList<ExchangeCell> read)
    {
        if (_slots.Count == 0 || read.Count == 0) return false;

        var deltas = new List<int>();
        foreach (var cell in read)
        {
            foreach (var slot in _slots)
            {
                if (!string.Equals(slot.Cell.Key, cell.Key, StringComparison.Ordinal)) continue;
                deltas.Add(CenterY(cell.Bounds) - CenterY(slot.Cell.Bounds));
                break;
            }
        }

        if (deltas.Count < MinScrollVotes) return false;
        deltas.Sort();
        return Math.Abs(deltas[deltas.Count / 2]) > MatchTolerancePx;
    }

    private static int CenterY(Rectangle r) => r.Top + r.Height / 2;

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
