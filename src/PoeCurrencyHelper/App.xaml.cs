using System.Threading;
using System.Windows;

namespace PoeCurrencyHelper;

public partial class App : System.Windows.Application
{
    // Single-instance guard: two copies would double the poe.ninja traffic and fight over config.json.
    // Held for the process lifetime; released implicitly on exit.
    private const string InstanceMutexName = "Global\\PoeCurrencyHelper.SingleInstance";
    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            // Already running — the existing window is the one the user wants. Bail before any UI is
            // created so we never flash a second panel.
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(e);
    }
}
