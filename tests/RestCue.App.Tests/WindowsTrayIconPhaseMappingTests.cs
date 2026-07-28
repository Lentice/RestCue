using System.Reflection;
using RestCue.App.Lifecycle;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class WindowsTrayIconPhaseMappingTests
{
    [Theory]
    [InlineData(WorkCyclePhase.Working, true, true)]
    [InlineData(WorkCyclePhase.PendingReminder, true, true)]
    [InlineData(WorkCyclePhase.ReminderVisible, true, true)]
    [InlineData(WorkCyclePhase.Snoozed, true, true)]
    [InlineData(WorkCyclePhase.Idle, true, false)]
    [InlineData(WorkCyclePhase.Paused, true, false)]
    [InlineData(WorkCyclePhase.FocusMode, false, true)]
    [InlineData(WorkCyclePhase.Disabled, false, false)]
    [InlineData(WorkCyclePhase.BreakInProgress, false, false)]
    public void TrayCommandAvailability_MatchesPhase(
        WorkCyclePhase phase,
        bool expectPauseEnabled,
        bool expectFocusEnabled)
    {
        var tray = new FakeTrayIcon();

        ApplyPhaseToTray(tray, phase);

        Assert.Equal(expectPauseEnabled, tray.PauseEnabled);
        Assert.Equal(expectFocusEnabled, tray.FocusModeEnabled);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Working)]
    [InlineData(WorkCyclePhase.PendingReminder)]
    [InlineData(WorkCyclePhase.ReminderVisible)]
    [InlineData(WorkCyclePhase.Snoozed)]
    [InlineData(WorkCyclePhase.Idle)]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.FocusMode)]
    [InlineData(WorkCyclePhase.BreakInProgress)]
    [InlineData(WorkCyclePhase.Disabled)]
    public void DisableCommand_AlwaysEnabled(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        ApplyPhaseToTray(tray, phase);
        Assert.True(tray.DisableEnabled);
    }

    [Fact]
    public void WindowsTrayIcon_SetPauseEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetPauseEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "_pauseItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetPauseEnabledTrue_EnablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetPauseEnabled(false);
        tray.SetPauseEnabled(true);
        Assert.True(GetMenuItemEnabled(tray, "_pauseItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetFocusModeEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetFocusModeEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "_focusItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetDisableEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetDisableEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "_disableItem"));
    }

    private static bool GetMenuItemEnabled(WindowsTrayIcon tray, string fieldName)
    {
        var field = typeof(WindowsTrayIcon)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        var menuItem = field!.GetValue(tray);
        var enabledProp = menuItem!.GetType().GetProperty("Enabled");
        return (bool)enabledProp!.GetValue(menuItem)!;
    }

    private static void ApplyPhaseToTray(FakeTrayIcon tray, WorkCyclePhase phase)
    {
        tray.SetSuppressedState(false);
        tray.SetPauseText(false);
        tray.SetPauseEnabled(true);
        tray.SetFocusModeText(false);
        tray.SetFocusModeEnabled(true);
        tray.SetDisableText(false);
        tray.SetDisableEnabled(true);

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                tray.SetPauseText(true);
                tray.SetFocusModeEnabled(false);
                break;

            case WorkCyclePhase.FocusMode:
                tray.SetFocusModeText(true);
                tray.SetPauseEnabled(false);
                break;

            case WorkCyclePhase.Disabled:
                tray.SetDisableText(true);
                tray.SetPauseEnabled(false);
                tray.SetFocusModeEnabled(false);
                break;

            case WorkCyclePhase.BreakInProgress:
                tray.SetPauseEnabled(false);
                tray.SetFocusModeEnabled(false);
                break;

            case WorkCyclePhase.Idle:
                tray.SetFocusModeEnabled(false);
                break;
        }
    }

    public sealed class FakeTrayIcon : ITrayIcon
    {
        private bool _visible;

#pragma warning disable CS0067
        public event EventHandler? OpenRequested;

        public event EventHandler? ExitRequested;

        public event EventHandler? PauseRequested;

        public event EventHandler? ResumeRequested;

        public event EventHandler? FocusModeRequested;

        public event EventHandler? EndFocusModeRequested;

        public event EventHandler? DisableRequested;

        public event EventHandler? EnableRequested;
#pragma warning restore CS0067

        public int VisibleSetToTrueCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool PauseEnabled { get; private set; } = true;

        public bool FocusModeEnabled { get; private set; } = true;

        public bool DisableEnabled { get; private set; } = true;

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (value)
                {
                    VisibleSetToTrueCount++;
                }
            }
        }

        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

        public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;

        public void SetPauseEnabled(bool enabled) => PauseEnabled = enabled;

        public void SetFocusModeEnabled(bool enabled) => FocusModeEnabled = enabled;

        public void SetDisableEnabled(bool enabled) => DisableEnabled = enabled;

        public void SetPauseText(bool isPaused)
        {
        }

        public void SetFocusModeText(bool isFocusMode)
        {
        }

        public void SetDisableText(bool isDisabled)
        {
        }

        public void SetStatusText(string text)
        {
        }

        public void SetSuppressedState(bool isSuppressed)
        {
        }
    }
}

public sealed class TrayCommandSafetyTests
{
    [Fact]
    public void Pause_ThrowsInvalidOperation_FromFocusMode()
    {
        var tracker = CreateTrackerInFocusMode();
        var ex = Record.Exception(() => tracker.Pause());
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void StartFocusMode_ThrowsInvalidOperation_FromDisabled()
    {
        var tracker = CreateTrackerInDisabled();
        var ex = Record.Exception(() => tracker.StartFocusMode());
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Resume_ThrowsInvalidOperation_WhenNotPaused()
    {
        var tracker = CreateDefaultTracker();
        var ex = Record.Exception(() => tracker.Resume());
        Assert.IsType<InvalidOperationException>(ex);
    }

    private static WorkCycleTracker CreateTrackerInFocusMode()
    {
        var tracker = CreateDefaultTracker();
        tracker.StartFocusMode();
        return tracker;
    }

    private static WorkCycleTracker CreateTrackerInDisabled()
    {
        var tracker = CreateDefaultTracker();
        tracker.Disable();
        return tracker;
    }

    private static WorkCycleTracker CreateDefaultTracker()
    {
        return new WorkCycleTracker(
            new RestCue.Infrastructure.Time.SystemClock(),
            TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(30));
    }
}
