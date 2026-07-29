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

        var trayOnlyArgs = new ReminderSuppressedEventArgs(showTrayCue: true);
        var silentArgs = new ReminderSuppressedEventArgs(showTrayCue: false);

        ApplyLowInterruptionHandler(tray, trayOnlyArgs);
        Assert.True(tray.IsSuppressed);
        Assert.Equal("RestCue – 休息提醒待處理", tray.StatusText);

        ApplyLowInterruptionHandler(tray, silentArgs);
        Assert.False(tray.IsSuppressed);
        Assert.Equal("RestCue – Eye Break Reminder", tray.StatusText);
    }

    [Fact]
    public void Fullscreen_sets_tray_cue()
    {
        var tray = new TrackingFakeTrayIcon();

        ApplyLowInterruptionHandler(tray, new ReminderSuppressedEventArgs(showTrayCue: true));

        Assert.True(tray.IsSuppressed);
        Assert.Equal("RestCue – 休息提醒待處理", tray.StatusText);
    }

    private static void ApplyLowInterruptionHandler(
        ITrayIcon tray, ReminderSuppressedEventArgs e)
    {
        if (e.ShowTrayCue)
        {
            tray.SetSuppressedState(true);
            tray.SetStatusText("RestCue – 休息提醒待處理");
        }
        else
        {
            tray.SetSuppressedState(false);
            tray.SetStatusText("RestCue – Eye Break Reminder");
        }
    }

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
#pragma warning restore CS0067

        public bool Visible { get; set; }
        public bool IsSuppressed { get; private set; }
        public string? StatusText { get; private set; }

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
    }
}
