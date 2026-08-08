using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class PresentationIntensityAppTests
{
    [Theory]
    [InlineData(RestDebtLevel.Level0, "RestCue – 監視中 (Level 0)")]
    [InlineData(RestDebtLevel.Level1, "RestCue – 輕微疲勞 (Level 1)")]
    [InlineData(RestDebtLevel.Level2, "RestCue – 明顯疲勞 (Level 2)")]
    [InlineData(RestDebtLevel.Level3, "RestCue – 需要休息 (Level 3)")]
    [InlineData(RestDebtLevel.Level4, "RestCue – 急需休息 (Level 4)")]
    public void GetStatusTextForDebtLevel_returns_expected_text(RestDebtLevel level, string expected)
    {
        Assert.Equal(expected, App.GetStatusTextForDebtLevel(level));
    }

    [Theory]
    [InlineData(WorkCyclePhase.Working, "RestCue – Eye Break Reminder")]
    [InlineData(WorkCyclePhase.PendingReminder, "RestCue – Eye Break Reminder")]
    [InlineData(WorkCyclePhase.ReminderVisible, "RestCue – Eye Break Reminder")]
    [InlineData(WorkCyclePhase.Snoozed, "RestCue – Eye Break Reminder")]
    [InlineData(WorkCyclePhase.Paused, "RestCue – 已暫停")]
    [InlineData(WorkCyclePhase.FocusMode, "RestCue – 專注模式")]
    [InlineData(WorkCyclePhase.Disabled, "RestCue – 已停用")]
    [InlineData(WorkCyclePhase.Idle, "RestCue – 離開中")]
    [InlineData(WorkCyclePhase.BreakInProgress, "RestCue – 休息中")]
    public void GetStatusTextForPhase_returns_expected_text(WorkCyclePhase phase, string expected)
    {
        Assert.Equal(expected, App.GetStatusTextForPhase(phase));
    }

    [Fact]
    public void GetStatusTextForDebtLevel_unknown_level_falls_back_to_level0()
    {
        var unknown = (RestDebtLevel)999;
        Assert.Equal("RestCue – 監視中 (Level 0)", App.GetStatusTextForDebtLevel(unknown));
    }

    [Fact]
    public void GetStatusTextForPhase_unknown_phase_falls_back()
    {
        var unknown = (WorkCyclePhase)999;
        Assert.Equal("RestCue – Eye Break Reminder", App.GetStatusTextForPhase(unknown));
    }

    [Fact]
    public void FakeTrayIcon_ApplyViewState_does_not_throw()
    {
        var tray = new FakeRecordingTrayIcon();
        tray.ApplyViewState(new TrayViewState(WorkCyclePhase.Working, RestDebtLevel.Level4, IsSuppressed: false));
    }
}

public sealed class FakeRecordingTrayIcon : ITrayIcon
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
    public string? LastStatusText { get; private set; }
    public TrayViewState? LastViewState { get; private set; }

    public void SetPauseText(bool isPaused) { }
    public void SetFocusModeText(bool isFocusMode) { }
    public void SetDisableText(bool isDisabled) { }
    public void SetStatusText(string text) => LastStatusText = text;
    public void SetPauseEnabled(bool enabled) { }
    public void SetFocusModeEnabled(bool enabled) { }
    public void SetDisableEnabled(bool enabled) { }
    public void SetBreakNowEnabled(bool enabled) { }
    public void ApplyViewState(TrayViewState state) => LastViewState = state;
    public void ShowLightTouchNotification(string title, string text, RestCue.Core.Settings.NotificationDuration duration) { }

    public void Dispose() { }
}
