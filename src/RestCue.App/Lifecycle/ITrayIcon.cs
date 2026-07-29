using RestCue.Core.Domain;

namespace RestCue.App.Lifecycle;

public interface ITrayIcon : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    event EventHandler? PauseRequested;

    event EventHandler? ResumeRequested;

    event EventHandler? FocusModeRequested;

    event EventHandler? EndFocusModeRequested;

    event EventHandler? DisableRequested;

    event EventHandler? EnableRequested;

    event EventHandler? BreakNowRequested;

    event EventHandler? StatisticsRequested;

    bool Visible { get; set; }

    void SetPauseText(bool isPaused);

    void SetFocusModeText(bool isFocusMode);

    void SetDisableText(bool isDisabled);

    void SetStatusText(string text);

    void SetPauseEnabled(bool enabled);

    void SetFocusModeEnabled(bool enabled);

    void SetDisableEnabled(bool enabled);

    void SetBreakNowEnabled(bool enabled);

    void SetSuppressedState(bool isSuppressed);

    void SetDebtLevel(RestDebtLevel level);
}