namespace RestCue.App.Lifecycle;

public sealed class ApplicationLifecycle : IApplicationLifecycle
{
    private readonly ITrayIcon trayIcon;
    private readonly IStatusWindow statusWindow;
    private readonly Action shutdown;
    private bool started;
    private bool disposed;

    public ApplicationLifecycle(ITrayIcon trayIcon, IStatusWindow statusWindow, Action shutdown)
    {
        this.trayIcon = trayIcon;
        this.statusWindow = statusWindow;
        this.shutdown = shutdown;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (started)
        {
            return;
        }

        trayIcon.OpenRequested += OnOpenRequested;
        trayIcon.ExitRequested += OnExitRequested;
        trayIcon.Visible = true;
        started = true;
    }

    public ITrayIcon TrayIcon => trayIcon;

    public void Exit()
    {
        Dispose();
        shutdown();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        trayIcon.OpenRequested -= OnOpenRequested;
        trayIcon.ExitRequested -= OnExitRequested;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        disposed = true;
    }

    private void OnOpenRequested(object? sender, EventArgs e) => statusWindow.ShowOrActivate();

    private void OnExitRequested(object? sender, EventArgs e) => Exit();
}
