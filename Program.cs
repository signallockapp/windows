using System;
using System.Threading;
using System.Windows.Forms;
using SignalLock.UI;

namespace SignalLock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard. A second launch silently exits.
        var mutex = new Mutex(initiallyOwned: true, name: "Global\\SignalLock.SingleInstance", out var firstInstance);
        if (!firstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Logging.Init();
        Log.App.Info("SignalLock starting");

        var state = new AppState();
        var tray = new TrayController(state);

        if (state.TrustedDevice is not null)
        {
            state.StartMonitoring();
        }

        Application.ApplicationExit += (_, _) =>
        {
            Log.App.Info("SignalLock exiting");
            state.Dispose();
            tray.Dispose();
        };

        Application.Run();
        GC.KeepAlive(mutex);
    }
}
