using System.Runtime.InteropServices;
using Velopack;

namespace PoeCurrencyHelper;

// Explicit process entry point. Velopack requires VelopackApp.Build().Run() to be the very first
// thing that executes: on install/update/uninstall the app is relaunched with hook arguments
// (e.g. --veloapp-install) that this call handles and then exits, before any of our own startup
// (the single-instance mutex) runs. With no hook args it returns and we start WPF exactly the way
// the auto-generated Main used to (App.xaml is now a Page, see csproj).
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    // Set once the catch below has logged a crash, so the AppDomain backstop (which also fires when
    // the catch rethrows) doesn't record the same exception a second time.
    private static volatile bool _crashLogged;

    [STAThread]
    public static void Main()
    {
        // Catch-all so a launch-time crash leaves a trace. This app is a WinExe, so an unhandled
        // exception never prints anywhere the user can see, and a failure inside Velopack init or
        // InitializeComponent() is exactly where startup crashes happen (see #27: instant exit, empty
        // console, no log). We write crash.log and
        // show a dialog pointing at it, then rethrow so the existing failure behaviour (non-zero exit,
        // Windows Error Reporting) is unchanged. The AppDomain hook covers fatal exceptions on other
        // foreground threads that bypass this try/catch.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (_crashLogged) return;
            AppPaths.LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        try
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            _crashLogged = true;
            ShowCrashDialog(ex, AppPaths.LogCrash("Startup", ex));
            throw;
        }
    }

    // Best-effort native message box (no dependency on the WPF/WinForms app, which may be the thing
    // that just failed) telling the user where the crash report landed so they can attach it.
    private static void ShowCrashDialog(Exception ex, string? logPath)
    {
        try
        {
            const uint MB_ICONERROR = 0x00000010;
            var location = logPath ?? "(a crash log could not be written)";
            MessageBox(IntPtr.Zero,
                $"Poe Currency Helper couldn't start.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"A full report was saved to:\n{location}\n\n" +
                "Please attach that file when reporting this on GitHub.",
                "Poe Currency Helper — startup error",
                MB_ICONERROR);
        }
        catch { /* the dialog is a courtesy; never let it mask the original crash */ }
    }
}
