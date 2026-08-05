using RestCue.App.Lifecycle;
using RestCue.Core.Activity;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using Xunit;
using FakeTrayIcon = RestCue.App.Tests.WindowsTrayIconPhaseMappingTests.FakeTrayIcon;

namespace RestCue.App.Tests;

/// <summary>
/// For a short window after launch every reminder command looked available and did
/// nothing: the tray icon became visible as soon as settings had loaded, but the tracker
/// was not created until the application rules had been read from the database and the
/// commands were wired later still. The command runner returned silently against a
/// missing tracker, so an early click produced no break, no pause, no error and no
/// acknowledgement.
/// </summary>
public sealed class UninitializedCommandSurfaceTests
{
    [Fact]
    public void The_tray_offers_no_reminder_command_before_initialisation()
    {
        var tray = new FakeTrayIcon();

        App.ApplyUninitializedToTray(tray);

        Assert.False(tray.PauseEnabled);
        Assert.False(tray.FocusModeEnabled);
        Assert.False(tray.DisableEnabled);
        Assert.False(tray.BreakNowEnabled);
    }

    [Fact]
    public void The_tray_tooltip_says_what_the_application_is_doing()
    {
        var tray = new FakeTrayIcon();

        App.ApplyUninitializedToTray(tray);

        Assert.Equal(App.StartingUpStatusText, tray.StatusText);
    }

    /// <summary>
    /// The ordering that matters: the tooltip and the disabled commands must already be in
    /// place at the instant the icon appears, not applied shortly afterwards.
    /// </summary>
    [Fact]
    public void The_tray_is_already_in_its_starting_state_when_the_icon_becomes_visible()
    {
        var tray = new RecordingTrayIcon();
        App.ApplyUninitializedToTray(tray);

        using var lifecycle = new ApplicationLifecycle(tray, new StubStatusWindow(), () => { });
        lifecycle.Start();

        Assert.Equal(App.StartingUpStatusText, tray.StatusTextWhenShown);
        Assert.False(tray.BreakNowEnabledWhenShown);
    }

    /// <summary>
    /// Once a phase is applied, availability comes from the policy again — the disabled
    /// opening state must not be sticky.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Availability_returns_to_the_policy_once_a_phase_is_applied(WorkCyclePhase phase)
    {
        var tray = new FakeTrayIcon();
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        App.ApplyUninitializedToTray(tray);
        App.ApplyPhaseToTray(tray, phase, RestDebtLevel.Level0);

        Assert.Equal(expected.PauseToggleEnabled, tray.PauseEnabled);
        Assert.Equal(expected.FocusToggleEnabled, tray.FocusModeEnabled);
        Assert.Equal(expected.DisableToggleEnabled, tray.DisableEnabled);
        Assert.Equal(expected.CanBreakNow, tray.BreakNowEnabled);
        Assert.NotEqual(App.StartingUpStatusText, tray.StatusText);
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

    /// <summary>Captures the tray's state at the moment it is made visible.</summary>
    private sealed class RecordingTrayIcon : ITrayIcon
    {
        private bool visible;
        private bool breakNowEnabled = true;

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

        public string? StatusText { get; private set; }

        public string? StatusTextWhenShown { get; private set; }

        public bool BreakNowEnabledWhenShown { get; private set; }

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (value)
                {
                    StatusTextWhenShown = StatusText;
                    BreakNowEnabledWhenShown = breakNowEnabled;
                }
            }
        }

        public void SetStatusText(string text) => StatusText = text;

        public void SetBreakNowEnabled(bool enabled) => breakNowEnabled = enabled;

        public void SetPauseEnabled(bool enabled) { }
        public void SetFocusModeEnabled(bool enabled) { }
        public void SetDisableEnabled(bool enabled) { }
        public void SetPauseText(bool isPaused) { }
        public void SetFocusModeText(bool isFocusMode) { }
        public void SetDisableText(bool isDisabled) { }
        public void SetSuppressedState(bool isSuppressed) { }
        public void SetDebtLevel(RestDebtLevel level) { }
        public void ShowLightTouchNotification(string title, string text, RestCue.Core.Settings.NotificationDuration duration) { }
        public void Dispose() { }
    }

    private sealed class StubStatusWindow : IStatusWindow
    {
#pragma warning disable CS0067
        public event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;
        public event EventHandler<SuggestionEventArgs>? SuggestionRequested;
#pragma warning restore CS0067

        public RestDebtLevel CurrentDebtLevel => RestDebtLevel.Level0;

        public void ShowOrActivate() { }
        public void StartBreakNow() { }
        public void Pause() { }
        public void Resume() { }
        public void StartFocusMode() { }
        public void EndFocusMode() { }
        public void Disable() { }
        public void Enable() { }
        public void PauseFor(TimeSpan duration) { }
        public void UpdateForegroundContextProvider(bool collectProcessNames) { }
        public void UpdateApplicationRules(IEnumerable<ApplicationRule> rules) { }
    }
}

/// <summary>
/// The same two states on the main window, against the real control tree.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class MainWindowInitializationGateTests
{
    private readonly WpfApplicationFixture wpf;

    public MainWindowInitializationGateTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    /// <summary>
    /// The markup enables every command button, which is where the dead interface came
    /// from. A freshly constructed window must not inherit that.
    /// </summary>
    [Fact]
    public void A_new_window_offers_no_reminder_command()
    {
        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                AssertAllCommandsDisabled(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// The tracker existing is not enough — startup wires the commands afterwards, so the
    /// surface stays dark until initialisation says otherwise.
    /// </summary>
    [Fact]
    public void Starting_activity_tracking_alone_does_not_make_the_commands_live()
    {
        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                StartTracking(window);

                Assert.NotNull(window.WorkCycleTracker);
                AssertAllCommandsDisabled(window);
            }
            finally
            {
                window.StopActivityTracking();
                window.Close();
            }
        });
    }

    [Fact]
    public void Completing_initialisation_hands_availability_to_the_policy()
    {
        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                StartTracking(window);
                window.CompleteCommandInitialization();

                CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(
                    window.WorkCycleTracker!.CurrentPhase);

                Assert.Equal(expected.PauseToggleEnabled, Button(window, "PauseResumeButton").IsEnabled);
                Assert.Equal(expected.FocusToggleEnabled, Button(window, "FocusButton").IsEnabled);
                Assert.Equal(expected.DisableToggleEnabled, Button(window, "DisableButton").IsEnabled);
                Assert.Equal(expected.CanBreakNow, Button(window, "BreakNowButton").IsEnabled);
                Assert.Equal(expected.CanBreakNow, MenuItem(window, "BreakNowMenuItem").IsEnabled);
            }
            finally
            {
                window.StopActivityTracking();
                window.Close();
            }
        });
    }

    private static void AssertAllCommandsDisabled(MainWindow window)
    {
        Assert.False(Button(window, "PauseResumeButton").IsEnabled);
        Assert.False(Button(window, "FocusButton").IsEnabled);
        Assert.False(Button(window, "DisableButton").IsEnabled);
        Assert.False(Button(window, "BreakNowButton").IsEnabled);

        Assert.False(MenuItem(window, "FocusMenuItem").IsEnabled);
        Assert.False(MenuItem(window, "DisableMenuItem").IsEnabled);
        Assert.False(MenuItem(window, "BreakNowMenuItem").IsEnabled);
    }

    private static void StartTracking(MainWindow window) =>
        window.StartActivityTracking(
            new IdleActivityMonitor(),
            AppSettings.Default,
            new FrozenClock());

    private static System.Windows.Controls.Button Button(MainWindow window, string name) =>
        (System.Windows.Controls.Button)(window.FindName(name)
            ?? throw new InvalidOperationException($"No button named {name}."));

    private static System.Windows.Controls.MenuItem MenuItem(MainWindow window, string name) =>
        (System.Windows.Controls.MenuItem)(window.FindName(name)
            ?? throw new InvalidOperationException($"No menu item named {name}."));

    private sealed class IdleActivityMonitor : IUserActivityMonitor
    {
        public UserActivitySample GetCurrentActivity() =>
            UserActivitySample.Available(TimeSpan.Zero);
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed => TimeSpan.Zero;
    }
}
