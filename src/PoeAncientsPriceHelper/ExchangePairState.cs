namespace PoeAncientsPriceHelper;

// The remembered "I Want" / "I Have" pair. Refreshed from every confirmed main-view read and
// consumed while a picker is open — the picker REPLACES the main view, so the opposite side's
// selection is not on screen at overlay time; this memory is what makes the picker badges possible.
// Owned by the scan loop (single-threaded) — no locking.
//
// Update rules:
//   unknown → known: applies in ONE read (a resolved name is high-confidence, like exact matches);
//   known → different / known → empty: needs 2 CONSECUTIVE identical reads, so a single OCR miss or
//   misread can't wipe or corrupt good state (mirrors the remnant panel's confirm-before-lock).
internal sealed class ExchangePairState
{
    private const int ConfirmReads = 2;

    public string? WantKey { get; private set; }
    public string? HaveKey { get; private set; }

    private string? _pendingWant, _pendingHave;
    private int _pendingWantCount, _pendingHaveCount;
    private bool _wantHasPending, _haveHasPending;   // pending null (a clear) is a real pending value

    public void ApplyMainView(string? wantKey, string? haveKey)
    {
        (WantKey, _pendingWant, _pendingWantCount, _wantHasPending) =
            ApplySide(WantKey, wantKey, _pendingWant, _pendingWantCount, _wantHasPending);
        (HaveKey, _pendingHave, _pendingHaveCount, _haveHasPending) =
            ApplySide(HaveKey, haveKey, _pendingHave, _pendingHaveCount, _haveHasPending);
    }

    // The ratio base for a picker on `pickerSide` — the OTHER side's currency, or null (no badge).
    public string? OppositeOf(ExchangeSide pickerSide) =>
        pickerSide == ExchangeSide.Want ? HaveKey : WantKey;

    public void Reset()
    {
        WantKey = HaveKey = null;
        _pendingWant = _pendingHave = null;
        _pendingWantCount = _pendingHaveCount = 0;
        _wantHasPending = _haveHasPending = false;
    }

    private static (string? Current, string? Pending, int Count, bool HasPending) ApplySide(
        string? current, string? incoming, string? pending, int count, bool hasPending)
    {
        if (incoming == current) return (current, null, 0, false);   // agreement clears any pending change
        if (current is null) return (incoming, null, 0, false);      // empty → set: instant
        if (hasPending && incoming == pending)
        {
            if (count + 1 >= ConfirmReads) return (incoming, null, 0, false);
            return (current, pending, count + 1, true);
        }
        return (current, incoming, 1, true);                          // new candidate change
    }
}
