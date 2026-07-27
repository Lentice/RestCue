namespace RestCue.App.Lifecycle;

public interface IApplicationLifecycle : IDisposable
{
    void Start();
}
