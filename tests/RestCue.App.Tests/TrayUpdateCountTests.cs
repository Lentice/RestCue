using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class TrayUpdateCountTests
{
    [Fact]
    public void Same_phase_suppressed_state_is_consistent()
    {
        var tray = new CountingTrayIcon();

        App.ApplyPhaseToTray(tray, WorkCyclePhase.Working, RestDebtLevel.Level0);

        Assert.False(tray.IsSuppressed);
    }

    [Fact]
    public void Different_phase_clears_suppressed_state()
    {
        var tray = new CountingTrayIcon();
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level0, IsSuppressed: true));

        App.ApplyPhaseToTray(tray, WorkCyclePhase.Paused, RestDebtLevel.Level0);

        Assert.False(tray.IsSuppressed);
    }

    [Fact]
    public void Different_debt_level_triggers_update()
    {
        var tray = new CountingTrayIcon();

        App.ApplyPhaseToTray(tray, WorkCyclePhase.Working, RestDebtLevel.Level0);
        App.ApplyPhaseToTray(tray, WorkCyclePhase.Working, RestDebtLevel.Level2);

        Assert.Equal(2, tray.ApplyViewStateCalls);
    }

    private sealed class CountingTrayIcon : ITrayIcon
    {
#pragma warning disable CS0067
        public event EventHandler? OpenRequested;
        public event EventHandler? ExitRequested;
        public event EventHandler? PauseRequested;
        public event EventHandler? ResumeRequested;
        public event EventHandler? FocusModeRequested;
        public event EventHandler? EndFocusModeRequested;
        public event EventHandler? DisableRequested;
        public event EventHandler? EnableRequested;
        public event EventHandler? BreakNowRequested;
        public event EventHandler? StatisticsRequested;
        public event EventHandler? SettingsRequested;
        public event EventHandler? AboutRequested;
        public event EventHandler? DataTransparencyRequested;
        public event EventHandler? DataManagementRequested;
        public event EventHandler<TimeSpan>? PauseForRequested;
#pragma warning restore CS0067

        public bool Visible { get; set; }

        public int ApplyViewStateCalls { get; private set; }
        public bool IsSuppressed => LastViewState?.IsSuppressed ?? false;
        public TrayViewState? LastViewState { get; private set; }

        public void ApplyViewState(TrayViewState state)
        {
            ApplyViewStateCalls++;
            LastViewState = state;
        }

        public void SetStatusText(string text) { }
        public void SetPauseText(bool isPaused) { }
        public void SetFocusModeText(bool isFocusMode) { }
        public void SetDisableText(bool isDisabled) { }
        public void SetPauseEnabled(bool enabled) { }
        public void SetFocusModeEnabled(bool enabled) { }
        public void SetDisableEnabled(bool enabled) { }
        public void SetBreakNowEnabled(bool enabled) { }
        public void ShowLightTouchNotification(string title, string text, RestCue.Core.Settings.NotificationDuration duration) { }

        public void Dispose() { }
    }
}
