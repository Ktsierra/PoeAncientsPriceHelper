namespace PoeCurrencyHelper;

// Single source of truth for where the app keeps state that must survive updates. Under the Velopack
// installed model the binaries live in %LocalAppData%\PoeCurrencyHelper\current\, and that
// `current\` folder is replaced wholesale on every update — so anything written next to the exe would
// be wiped. DataDir resolves to %LocalAppData%\PoeCurrencyHelper (the *parent* of `current\`,
// which the updater never touches), so config, the icon cache and user overrides persist across
// updates. Local (not Roaming): the calibration region is monitor-specific and must not roam between
// machines. Tests bypass this entirely by passing an explicit dir to ConfigStore/IconCache.
internal static class AppPaths
{
    private static readonly Lazy<string> LazyDataDir = new(() =>
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PoeCurrencyHelper");
        Directory.CreateDirectory(dir);
        return dir;
    });

    public static string DataDir => LazyDataDir.Value;

    // Append-only breadcrumb log for the auto-updater. The update path is otherwise silent (failures
    // are swallowed so a flaky GitHub never bothers the user), which makes field issues impossible to
    // diagnose — this gives a minimal trail in %LocalAppData%\...\update.log without any UI noise.
    public static void LogUpdate(string message)
    {
        try { File.AppendAllText(Path.Combine(DataDir, "update.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { /* logging must never throw */ }
    }

    // Records a fatal exception to crash.log and returns the path so the caller can point the user at
    // it. Until #27, a launch-time crash left no trace at all: the app is a WinExe (nothing prints to
    // a console) and the --debug console isn't attached until partway through App.OnStartup — past the
    // Velopack init and InitializeComponent() where startup actually fails. crash.log lives in DataDir,
    // not next to the exe, so it's always writable and survives the Velopack `current\` swap.
    // Best-effort: returns null if the file couldn't be written; logging must never throw.
    public static string? LogCrash(string context, Exception? ex)
    {
        try
        {
            var path = Path.Combine(DataDir, "crash.log");
            File.AppendAllText(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{context}] {ex?.ToString() ?? "(no exception object)"}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
            return path;
        }
        catch { return null; }
    }
}
