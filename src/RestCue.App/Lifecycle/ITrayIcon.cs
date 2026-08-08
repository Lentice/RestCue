using RestCue.Core.Settings;

namespace RestCue.App.Lifecycle;

public interface ITrayIcon : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    event EventHandler? PauseRequested;

    event EventHandler<TimeSpan>? PauseForRequested;

    event EventHandler? ResumeRequested;

    event EventHandler? FocusModeRequested;

    event EventHandler? EndFocusModeRequested;

    event EventHandler? DisableRequested;

    event EventHandler? EnableRequested;

    event EventHandler? BreakNowRequested;

    event EventHandler? StatisticsRequested;

    event EventHandler? SettingsRequested;

    event EventHandler? AboutRequested;

    event EventHandler? DataTransparencyRequested;

    event EventHandler? DataManagementRequested;

    bool Visible { get; set; }

    void SetPauseText(bool isPaused);

    void SetFocusModeText(bool isFocusMode);

    void SetDisableText(bool isDisabled);

    void SetStatusText(string text);

    void SetPauseEnabled(bool enabled);

    void SetFocusModeEnabled(bool enabled);

    void SetDisableEnabled(bool enabled);

    void SetBreakNowEnabled(bool enabled);

    /// <summary>
    /// Applies the tray's presentation state. The icon and tooltip are derived from the
    /// state together, so mode, rest debt and suppression cannot be shown inconsistently.
    /// </summary>
    void ApplyViewState(TrayViewState state);

    void ShowLightTouchNotification(string title, string text, NotificationDuration duration);
}
