using System.Windows;
using RestCue.App.Lifecycle;

namespace RestCue.App;

public partial class App : System.Windows.Application
{
    private ApplicationLifecycle? _lifecycle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var statusWindow = new MainWindow();
        _lifecycle = new ApplicationLifecycle(
            new WindowsTrayIcon(),
            statusWindow,
            Shutdown);
        _lifecycle.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifecycle?.Dispose();
        base.OnExit(e);
    }
}
