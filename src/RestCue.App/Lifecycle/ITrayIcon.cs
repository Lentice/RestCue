namespace RestCue.App.Lifecycle;

public interface ITrayIcon : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    bool Visible { get; set; }
}
