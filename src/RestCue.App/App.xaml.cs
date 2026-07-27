using System.Diagnostics;
using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Settings;

namespace RestCue.App;

public partial class App : System.Windows.Application
{
    private ApplicationLifecycle? _lifecycle;
    private ApplicationStartup? _startup;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var statusWindow = new MainWindow();
        _lifecycle = new ApplicationLifecycle(
            new WindowsTrayIcon(),
            statusWindow,
            Shutdown);
        var settingsRepository = new SqliteSettingsRepository(
            LocalSettingsPaths.DatabaseFile,
            new AppSettingsValidator());
        _startup = new ApplicationStartup(settingsRepository, _lifecycle);
        try
        {
            await _startup.InitializeAsync();
            statusWindow.StartActivityTracking(
                new WindowsUserActivityMonitor(),
                _startup.CurrentSettings);
        }
        catch (Exception exception)
        {
            ApplicationStartupFailureHandler.Handle(
                exception,
                message => Trace.TraceError(message),
                Shutdown);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifecycle?.Dispose();
        base.OnExit(e);
    }
}
