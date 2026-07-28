namespace RestCue.App.Lifecycle;

public interface IStatusWindow
{
    void ShowOrActivate();

    void StartBreakNow();

    void Pause();

    void Resume();

    void StartFocusMode();

    void EndFocusMode();

    void Disable();

    void Enable();
}
