using System.Windows;
using WorkingCenter.Core;
using WorkingCenter.Views;
// Disambiguate from System.Windows.Forms.Application (pulled in by UseWindowsForms for NotifyIcon).
using Application = System.Windows.Application;

namespace WorkingCenter;

public partial class App : Application
{
    private AppState? _state;
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstance();
        if (!_singleInstance.TryAcquire(() =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => _state?.ShowFromTray()));
        }))
        {
            Shutdown();
            return;
        }

        Logger.Init();
        Logger.Info("=== Hive Station v0.1.0 starting ===");
        ThemeService.Apply(ThemeService.Load());  // apply persisted theme before UI shows

        _state = new AppState();
        _state.Start();
        // Flush notes on logoff/shutdown so debounced writes aren't lost.
        SessionEnding += (_, _) => _state.FlushNotes();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _state?.Shutdown();
            Logger.Info("=== Hive Station exited ===");
        }
        finally
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
