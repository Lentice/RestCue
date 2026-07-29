using System.Reflection;
using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class WindowsTrayIconPhaseMappingTests
{
    [Theory]
    [InlineData(WorkCyclePhase.Working, true, true, true)]
    [InlineData(WorkCyclePhase.PendingReminder, true, true, true)]
    [InlineData(WorkCyclePhase.ReminderVisible, true, true, true)]
    [InlineData(WorkCyclePhase.Snoozed, true, true, true)]
    [InlineData(WorkCyclePhase.Idle, true, false, false)]
    [InlineData(WorkCyclePhase.Paused, true, false, false)]
    [InlineData(WorkCyclePhase.FocusMode, false, true, true)]
    [InlineData(WorkCyclePhase.Disabled, false, false, false)]
    [InlineData(WorkCyclePhase.BreakInProgress, false, false, false)]
    public void TrayCommandAvailability_MatchesPhase(
        WorkCyclePhase phase,
        bool expectPauseEnabled,
        bool expectFocusEnabled,
        bool expectBreakNowEnabled)
    {
        var tray = new FakeTrayIcon();

        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);

        Assert.Equal(expectPauseEnabled, tray.PauseEnabled);
        Assert.Equal(expectFocusEnabled, tray.FocusModeEnabled);
        Assert.Equal(expectBreakNowEnabled, tray.BreakNowEnabled);
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
        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);
        Assert.True(tray.DisableEnabled);
    }

    [Fact]
    public void Paused_clears_suppressed_state()
    {
        var tray = new FakeTrayIcon();
        tray.SetSuppressedState(true);

        App.ApplyPhaseToTray(tray, WorkCyclePhase.Paused, RestDebtLevel.Level0);

        Assert.False(tray.IsSuppressed);
        Assert.Equal("RestCue – 已暫停", tray.StatusText);
        Assert.True(tray.PauseEnabled);
        Assert.False(tray.FocusModeEnabled);
        Assert.False(tray.BreakNowEnabled);
    }

    [Fact]
    public void FocusMode_clears_suppressed_state()
    {
        var tray = new FakeTrayIcon();
        tray.SetSuppressedState(true);

        App.ApplyPhaseToTray(tray, WorkCyclePhase.FocusMode, RestDebtLevel.Level0);

        Assert.False(tray.IsSuppressed);
        Assert.Equal("RestCue – 專注模式", tray.StatusText);
        Assert.False(tray.PauseEnabled);
        Assert.True(tray.FocusModeEnabled);
        Assert.True(tray.BreakNowEnabled);
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

    [Fact]
    public void WindowsTrayIcon_SetBreakNowEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetBreakNowEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "_breakNowItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetBreakNowEnabledTrue_EnablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetBreakNowEnabled(false);
        tray.SetBreakNowEnabled(true);
        Assert.True(GetMenuItemEnabled(tray, "_breakNowItem"));
    }

    [Fact]
    public void WindowsTrayIcon_BreakNowItemClick_RaisesBreakNowRequested()
    {
        using var tray = new WindowsTrayIcon();
        bool invoked = false;
        tray.BreakNowRequested += (_, _) => invoked = true;

        var field = typeof(WindowsTrayIcon)
            .GetField("_breakNowItem", BindingFlags.NonPublic | BindingFlags.Instance);
        var menuItem = field!.GetValue(tray);
        var performClick = menuItem!.GetType().GetMethod("PerformClick", Type.EmptyTypes);
        performClick!.Invoke(menuItem, null);

        Assert.True(invoked);
    }

    private static bool GetMenuItemEnabled(WindowsTrayIcon tray, string fieldName)
    {
        var field = typeof(WindowsTrayIcon)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        var menuItem = field!.GetValue(tray);
        var enabledProp = menuItem!.GetType().GetProperty("Enabled");
        return (bool)enabledProp!.GetValue(menuItem)!;
    }

    [Fact]
    public void BreakNowRequested_event_invokes_wired_handler()
    {
        var tray = new FakeTrayIcon();
        var invoked = false;
        tray.BreakNowRequested += (_, _) => invoked = true;
        tray.RequestBreakNow();
        Assert.True(invoked);
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

        public event EventHandler? BreakNowRequested;

        public event EventHandler? StatisticsRequested;

        public event EventHandler? SettingsRequested;

        public event EventHandler? AboutRequested;
#pragma warning restore CS0067

        public int VisibleSetToTrueCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool PauseEnabled { get; private set; } = true;

        public bool FocusModeEnabled { get; private set; } = true;

        public bool DisableEnabled { get; private set; } = true;

        public bool BreakNowEnabled { get; private set; } = true;

        public bool IsSuppressed { get; private set; }

        public string? StatusText { get; private set; }

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

        public void RequestBreakNow() => BreakNowRequested?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;

        public void SetPauseEnabled(bool enabled) => PauseEnabled = enabled;

        public void SetFocusModeEnabled(bool enabled) => FocusModeEnabled = enabled;

        public void SetDisableEnabled(bool enabled) => DisableEnabled = enabled;

        public void SetBreakNowEnabled(bool enabled) => BreakNowEnabled = enabled;

        public void SetPauseText(bool isPaused)
        {
        }

        public void SetFocusModeText(bool isFocusMode)
        {
        }

        public void SetDisableText(bool isDisabled)
        {
        }

        public void SetStatusText(string text) => StatusText = text;

        public void SetSuppressedState(bool isSuppressed) => IsSuppressed = isSuppressed;

        public void SetDebtLevel(RestDebtLevel level) { }
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
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(20));
    }
}
