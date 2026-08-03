using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class TrayCueSuppressionTests
{
    [Fact]
    public void Silent_clears_tray_cue_set_by_TrayOnly()
    {
        var tray = new TrackingFakeTrayIcon();

        Apply(tray, new ReminderSuppressedEventArgs(showTrayCue: true));
        Assert.True(tray.IsSuppressed);
        Assert.Equal(App.PendingReminderStatusText, tray.StatusText);

        Apply(tray, new ReminderSuppressedEventArgs(showTrayCue: false));
        Assert.False(tray.IsSuppressed);
        Assert.Equal("RestCue – Eye Break Reminder", tray.StatusText);
    }

    [Fact]
    public void Fullscreen_sets_tray_cue()
    {
        var tray = new TrackingFakeTrayIcon();

        Apply(tray, new ReminderSuppressedEventArgs(showTrayCue: true));

        Assert.True(tray.IsSuppressed);
        Assert.Equal(App.PendingReminderStatusText, tray.StatusText);
    }

    [Fact]
    public void Light_touch_shows_a_cue_a_toast_and_a_sound()
    {
        var tray = new TrackingFakeTrayIcon();
        int sounds = 0;

        App.ApplyLightTouchReminderToTray(tray, soundEnabled: true, () => sounds++);

        Assert.True(tray.IsSuppressed);
        Assert.Equal(App.PendingReminderStatusText, tray.StatusText);
        Assert.Equal("RestCue – 休息提醒", tray.NotifiedTitle);
        Assert.NotNull(tray.NotifiedText);
        Assert.Equal(1, sounds);
    }

    [Fact]
    public void Light_touch_stays_silent_when_the_user_disabled_the_sound()
    {
        var tray = new TrackingFakeTrayIcon();
        int sounds = 0;

        App.ApplyLightTouchReminderToTray(tray, soundEnabled: false, () => sounds++);

        // The cue and the toast still happen; only the sound is withheld.
        Assert.True(tray.IsSuppressed);
        Assert.NotNull(tray.NotifiedTitle);
        Assert.Equal(0, sounds);
    }

    [Theory]
    [InlineData(RestDebtLevel.Level1)]
    [InlineData(RestDebtLevel.Level2)]
    [InlineData(RestDebtLevel.Level3)]
    [InlineData(RestDebtLevel.Level4)]
    public void Debt_level_notification_shows_a_tray_balloon_for_each_level(RestDebtLevel level)
    {
        var tray = new TrackingFakeTrayIcon();

        App.ApplyDebtLevelNotificationToTray(tray, level, showNotification: true);

        Assert.Equal(App.GetStatusTextForDebtLevel(level), tray.NotifiedTitle);
        Assert.Contains("休息需求", tray.NotifiedText);
    }

    [Fact]
    public void Debt_level_notification_can_be_disabled()
    {
        var tray = new TrackingFakeTrayIcon();

        App.ApplyDebtLevelNotificationToTray(
            tray, RestDebtLevel.Level2, showNotification: false);

        Assert.Null(tray.NotifiedTitle);
        Assert.Null(tray.NotifiedText);
    }

    /// <summary>
    /// Calls the shipping handler. This test used to reimplement it, which meant the
    /// behaviour was untested while appearing covered.
    /// </summary>
    private static void Apply(ITrayIcon tray, ReminderSuppressedEventArgs e) =>
        App.ApplySuppressedReminderToTray(tray, e.ShowTrayCue);

    private sealed class TrackingFakeTrayIcon : ITrayIcon
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
        public bool IsSuppressed { get; private set; }
        public string? StatusText { get; private set; }
        public string? NotifiedTitle { get; private set; }
        public string? NotifiedText { get; private set; }

        public void SetSuppressedState(bool isSuppressed) => IsSuppressed = isSuppressed;

        public void SetStatusText(string text) => StatusText = text;

        public void SetDebtLevel(RestDebtLevel level) { }

        public void Dispose() { }
        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
        public void SetPauseEnabled(bool enabled) { }
        public void SetFocusModeEnabled(bool enabled) { }
        public void SetDisableEnabled(bool enabled) { }
        public void SetBreakNowEnabled(bool enabled) { }
        public void SetPauseText(bool isPaused) { }
        public void SetFocusModeText(bool isFocusMode) { }
        public void SetDisableText(bool isDisabled) { }
        public void ShowLightTouchNotification(string title, string text)
        {
            NotifiedTitle = title;
            NotifiedText = text;
        }
    }
}
