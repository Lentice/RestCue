namespace RestCue.App.Lifecycle;

public sealed class ApplicationLifecycle : IDisposable
{
    private readonly ITrayIcon _trayIcon;
    private readonly IStatusWindow _statusWindow;
    private readonly Action _shutdown;
    private bool _started;
    private bool _disposed;

    public ApplicationLifecycle(ITrayIcon trayIcon, IStatusWindow statusWindow, Action shutdown)
    {
        _trayIcon = trayIcon;
        _statusWindow = statusWindow;
        _shutdown = shutdown;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        _trayIcon.OpenRequested += OnOpenRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.Visible = true;
        _started = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.OpenRequested -= OnOpenRequested;
        _trayIcon.ExitRequested -= OnExitRequested;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _disposed = true;
    }

    private void OnOpenRequested(object? sender, EventArgs e) => _statusWindow.ShowOrActivate();

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Dispose();
        _shutdown();
    }
}
