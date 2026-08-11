using System.Reflection;
using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class WindowsTrayIconPhaseMappingTests
{
    /// <summary>
    /// The tray applier must produce exactly what the availability policy dictates, for
    /// every phase. Asserted against the policy rather than against a second hand-written
    /// table, because a second table is how the tray and the main window drifted apart in
    /// the first place. The policy's own table is asserted — and checked against what the
    /// engine accepts — in the core policy suite.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void TrayCommandAvailability_MatchesPolicy(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);

        Assert.Equal(expected.PauseToggleEnabled, tray.PauseEnabled);
        Assert.Equal(expected.FocusToggleEnabled, tray.FocusModeEnabled);
        Assert.Equal(expected.DisableToggleEnabled, tray.DisableEnabled);
        Assert.Equal(expected.CanBreakNow, tray.BreakNowEnabled);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void TrayCommandLabels_MatchPolicy(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);

        Assert.Equal(expected.ShowResume, tray.ShowsResumeText);
        Assert.Equal(expected.ShowEndFocusMode, tray.ShowsEndFocusModeText);
        Assert.Equal(expected.ShowEnable, tray.ShowsEnableText);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void DisableCommand_IsReachableInEveryPhase(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);
        Assert.True(tray.DisableEnabled);
    }

    public static TheoryData<WorkCyclePhase> AllPhases()
    {
        var data = new TheoryData<WorkCyclePhase>();
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            data.Add(phase);
        }
        return data;
    }

    [Fact]
    public void Paused_clears_suppressed_state()
    {
        var tray = new FakeTrayIcon();
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level0, IsSuppressed: true));

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
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level0, IsSuppressed: true));

        App.ApplyPhaseToTray(tray, WorkCyclePhase.FocusMode, RestDebtLevel.Level0);

        Assert.False(tray.IsSuppressed);
        Assert.Equal("RestCue – 專注模式", tray.StatusText);
        Assert.False(tray.PauseEnabled);
        Assert.True(tray.FocusModeEnabled);
        Assert.True(tray.BreakNowEnabled);
    }

    [Fact]
    public void Live_status_summary_replaces_the_default_active_tooltip()
    {
        var tray = new FakeTrayIcon();

        App.ApplyPhaseToTray(
            tray,
            WorkCyclePhase.Working,
            RestDebtLevel.Level0,
            "RestCue｜有效工作 7分｜距休息需求 約13分｜L0");

        Assert.Equal("RestCue｜有效工作 7分｜距休息需求 約13分｜L0", tray.StatusText);
    }

    [Theory]
    [InlineData(WorkCyclePhase.FocusMode, RestDebtLevel.Level2, "RestCue – 專注模式 · 明顯疲勞 (Level 2)")]
    [InlineData(WorkCyclePhase.Paused, RestDebtLevel.Level4, "RestCue – 已暫停 · 急需休息 (Level 4)")]
    [InlineData(WorkCyclePhase.Disabled, RestDebtLevel.Level4, "RestCue – 已停用 · 急需休息 (Level 4)")]
    [InlineData(WorkCyclePhase.Working, RestDebtLevel.Level2, "RestCue – 明顯疲勞 (Level 2)")]
    public void ApplyPhaseToTray_tooltip_names_mode_and_debt_together(
        WorkCyclePhase phase, RestDebtLevel level, string expected)
    {
        var tray = new FakeTrayIcon();

        App.ApplyPhaseToTray(tray, phase, level);

        Assert.Equal(expected, tray.StatusText);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Debt_change_refreshes_tooltip_in_every_phase(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level1);
        string before = tray.StatusText!;

        App.ApplyDebtLevelChangeToTray(tray, phase, RestDebtLevel.Level3, isSuppressed: false);

        Assert.NotEqual(before, tray.StatusText);
        Assert.Contains("Level 3", tray.StatusText);
    }

    [Fact]
    public void Debt_change_while_a_reminder_is_pending_keeps_the_cue_text_with_the_new_level()
    {
        var tray = new FakeTrayIcon();
        App.ApplySuppressedReminderToTray(
            tray,
            showTrayCue: true,
            level: RestDebtLevel.Level1,
            phase: WorkCyclePhase.PendingReminder);

        App.ApplyDebtLevelChangeToTray(
            tray, WorkCyclePhase.PendingReminder, RestDebtLevel.Level4, isSuppressed: true);

        Assert.Equal("RestCue – 急需休息 (Level 4) · 休息提醒待處理", tray.StatusText);
    }

    [Fact]
    public void ApplyPhaseToTray_PauseAtLevel4_DoesNotLeaveTheUrgentIcon()
    {
        using var tray = new WindowsTrayIcon();

        App.ApplyPhaseToTray(tray, WorkCyclePhase.Paused, RestDebtLevel.Level4);

        Assert.Same(GetStaticIcon("RemindersOffIcon"), GetCurrentIcon(tray));
        Assert.NotSame(GetStaticIcon("Level4Icon"), GetCurrentIcon(tray));
        Assert.Equal("RestCue – 已暫停 · 急需休息 (Level 4)", GetCurrentText(tray));
    }

    [Fact]
    public void WindowsTrayIcon_SetPauseEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetPauseEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "pauseItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetPauseEnabledTrue_EnablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetPauseEnabled(false);
        tray.SetPauseEnabled(true);
        Assert.True(GetMenuItemEnabled(tray, "pauseItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetFocusModeEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetFocusModeEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "focusItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetDisableEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetDisableEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "disableItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetBreakNowEnabledFalse_DisablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetBreakNowEnabled(false);
        Assert.False(GetMenuItemEnabled(tray, "breakNowItem"));
    }

    [Fact]
    public void WindowsTrayIcon_SetBreakNowEnabledTrue_EnablesMenuItem()
    {
        using var tray = new WindowsTrayIcon();
        tray.SetBreakNowEnabled(false);
        tray.SetBreakNowEnabled(true);
        Assert.True(GetMenuItemEnabled(tray, "breakNowItem"));
    }

    [Fact]
    public void WindowsTrayIcon_BreakNowItemClick_RaisesBreakNowRequested()
    {
        using var tray = new WindowsTrayIcon();
        bool invoked = false;
        tray.BreakNowRequested += (_, _) => invoked = true;

        var field = typeof(WindowsTrayIcon)
            .GetField("breakNowItem", BindingFlags.NonPublic | BindingFlags.Instance);
        var menuItem = field!.GetValue(tray);
        var performClick = menuItem!.GetType().GetMethod("PerformClick", Type.EmptyTypes);
        performClick!.Invoke(menuItem, null);

        Assert.True(invoked);
    }

    [Theory]
    [InlineData(RestDebtLevel.Level0, "SuppressedIcon")]
    [InlineData(RestDebtLevel.Level1, "Level1Icon")]
    [InlineData(RestDebtLevel.Level4, "Level4Icon")]
    public void WindowsTrayIcon_PendingReminder_KeepsDebtColourAboveLevel0(
        RestDebtLevel level, string expectedIconField)
    {
        using var tray = new WindowsTrayIcon();
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.PendingReminder, level, IsSuppressed: true));

        Assert.Same(GetStaticIcon(expectedIconField), GetCurrentIcon(tray));
    }

    [Fact]
    public void WindowsTrayIcon_DebtRisingWhileReminderPending_UpdatesIcon()
    {
        using var tray = new WindowsTrayIcon();
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level2, IsSuppressed: true));

        Assert.Same(GetStaticIcon("Level2Icon"), GetCurrentIcon(tray));
    }

    [Fact]
    public void WindowsTrayIcon_FocusMode_KeepsTheDebtIcon()
    {
        using var tray = new WindowsTrayIcon();
        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.FocusMode, RestDebtLevel.Level2, IsSuppressed: false));

        Assert.Same(GetStaticIcon("Level2Icon"), GetCurrentIcon(tray));
    }

    [Theory]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.Disabled)]
    public void WindowsTrayIcon_ModeAtLevel4_ShowsRemindersOffNotUrgentDebtIcon(WorkCyclePhase mode)
    {
        using var tray = new WindowsTrayIcon();
        tray.ApplyViewState(new TrayViewState(mode, RestDebtLevel.Level4, IsSuppressed: false));

        Assert.Same(GetStaticIcon("RemindersOffIcon"), GetCurrentIcon(tray));
        Assert.NotSame(GetStaticIcon("Level4Icon"), GetCurrentIcon(tray));
    }

    [Theory]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.FocusMode)]
    [InlineData(WorkCyclePhase.Disabled)]
    public void WindowsTrayIcon_LeavingAMode_RestoresTheDebtIconAndTooltip(WorkCyclePhase mode)
    {
        using var tray = new WindowsTrayIcon();
        tray.ApplyViewState(new TrayViewState(mode, RestDebtLevel.Level3, IsSuppressed: false));

        tray.ApplyViewState(
            new TrayViewState(WorkCyclePhase.Working, RestDebtLevel.Level3, IsSuppressed: false));

        Assert.Same(GetStaticIcon("Level3Icon"), GetCurrentIcon(tray));
        Assert.Contains("Level 3", GetCurrentText(tray));
    }

    [Fact]
    public void WindowsTrayIcon_DebtLevelIcons_DifferInGreyscale()
    {
        string[] levelFields = ["NormalIcon", "Level1Icon", "Level2Icon", "Level3Icon", "Level4Icon"];
        byte[][] greys = levelFields
            .Select(field => GreyscalePixels((Icon)GetStaticIcon(field)!))
            .ToArray();

        for (int i = 0; i < greys.Length; i++)
        {
            for (int j = i + 1; j < greys.Length; j++)
            {
                Assert.False(
                    greys[i].AsSpan().SequenceEqual(greys[j]),
                    $"Level icons {levelFields[i]} and {levelFields[j]} are indistinguishable in greyscale.");
            }
        }
    }

    private static byte[] GreyscalePixels(Icon icon)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawIcon(icon, new Rectangle(0, 0, 32, 32));
        }

        var pixels = new byte[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                pixels[(y * 32) + x] = (byte)((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B));
            }
        }

        return pixels;
    }

    private static object? GetStaticIcon(string fieldName) =>
        typeof(WindowsTrayIcon)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null);

    private static object? GetCurrentIcon(WindowsTrayIcon tray)
    {
        var notifyIcon = typeof(WindowsTrayIcon)
            .GetField("notifyIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(tray);
        return notifyIcon!.GetType().GetProperty("Icon")!.GetValue(notifyIcon);
    }

    private static string? GetCurrentText(WindowsTrayIcon tray)
    {
        var notifyIcon = typeof(WindowsTrayIcon)
            .GetField("notifyIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(tray);
        return (string?)notifyIcon!.GetType().GetProperty("Text")!.GetValue(notifyIcon);
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
        private bool visible;

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

        public int VisibleSetToTrueCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool PauseEnabled { get; private set; } = true;

        public bool FocusModeEnabled { get; private set; } = true;

        public bool DisableEnabled { get; private set; } = true;

        public bool BreakNowEnabled { get; private set; } = true;

        public bool IsSuppressed => LastViewState?.IsSuppressed ?? false;

        public string? StatusText { get; private set; }

        public TrayViewState? LastViewState { get; private set; }

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
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

        public bool ShowsResumeText { get; private set; }

        public bool ShowsEndFocusModeText { get; private set; }

        public bool ShowsEnableText { get; private set; }

        public void SetPauseText(bool isPaused) => ShowsResumeText = isPaused;

        public void SetFocusModeText(bool isFocusMode) => ShowsEndFocusModeText = isFocusMode;

        public void SetDisableText(bool isDisabled) => ShowsEnableText = isDisabled;

        public void SetStatusText(string text) => StatusText = text;

        public void ApplyViewState(TrayViewState state)
        {
            LastViewState = state;
            StatusText = App.GetTrayTooltip(state);
        }

        public void ShowLightTouchNotification(string title, string text, RestCue.Core.Settings.NotificationDuration duration) { }
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
