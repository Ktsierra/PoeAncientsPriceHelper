namespace PoeAncientsPriceHelper;

// Debug-only Currency Exchange tracing, mirroring RumourDiag. Writes to "exchange_scan.txt" in the
// data dir (next to scan_log.txt / rumour_scan.txt), only under --debug; otherwise every call is a
// no-op so the hot loop pays nothing.
//
// Why this exists: every failure mode in the exchange path ends at the same silent HideOverlay() —
// no prices yet, gate saw nothing, cells didn't resolve, no remembered pair, dismiss latched. From
// the user's chair those are indistinguishable from a crash, and the only other signal is two
// Console.Error lines that go nowhere in a WinExe. The first Windows test round burned an afternoon
// on that; this is the trail that makes the next one a bug report instead of a guess.
internal static class ExchangeDiag
{
    private static readonly object Gate = new();
    private static readonly string LogPath = System.IO.Path.Combine(AppPaths.DataDir, "exchange_scan.txt");
    private static bool _started;

    // Set once per hide so a stationary "nothing is happening" state logs its reason a single time
    // instead of ~1 line/second forever. Cleared whenever the overlay actually shows again.
    private static string _lastQuiet = "";

    public static void Log(string msg)
    {
        if (!App.DebugMode) return;
        lock (Gate)
        {
            try
            {
                if (!_started) { File.WriteAllText(LogPath, ""); _started = true; }
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { /* logging must never break the app */ }
        }
    }

    // Log a recurring "still nothing, because X" reason only when X changes. The exchange loop ticks
    // ~1 Hz forever while idle; without this the useful lines drown.
    public static void Quiet(string reason)
    {
        if (!App.DebugMode) return;
        lock (Gate)
        {
            if (reason == _lastQuiet) return;
            _lastQuiet = reason;
        }
        Log(reason);
    }

    public static void ClearQuiet()
    {
        lock (Gate) _lastQuiet = "";
    }
}
