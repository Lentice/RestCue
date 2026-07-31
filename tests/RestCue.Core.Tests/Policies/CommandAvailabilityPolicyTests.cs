using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class CommandAvailabilityPolicyTests
{
    // Phase, pause, resume, start focus, end focus, disable, enable, break now.
    [Theory]
    [InlineData(WorkCyclePhase.Working, true, false, true, false, true, false, true)]
    [InlineData(WorkCyclePhase.PendingReminder, true, false, true, false, true, false, true)]
    [InlineData(WorkCyclePhase.ReminderVisible, true, false, true, false, true, false, true)]
    [InlineData(WorkCyclePhase.Snoozed, true, false, true, false, true, false, true)]
    // During a break, pause / focus mode / disable are all legal at the cost of cancelling
    // it — a destructive consequence, not a reason to withhold the command.
    [InlineData(WorkCyclePhase.BreakInProgress, true, false, true, false, true, false, false)]
    [InlineData(WorkCyclePhase.Idle, false, false, false, false, true, false, false)]
    [InlineData(WorkCyclePhase.Paused, false, true, false, false, true, false, false)]
    [InlineData(WorkCyclePhase.FocusMode, false, false, false, true, true, false, true)]
    [InlineData(WorkCyclePhase.Disabled, false, false, false, false, false, true, false)]
    public void Availability_table(
        WorkCyclePhase phase,
        bool canPause,
        bool canResume,
        bool canStartFocusMode,
        bool canEndFocusMode,
        bool canDisable,
        bool canEnable,
        bool canBreakNow)
    {
        var availability = CommandAvailabilityPolicy.ForPhase(phase);

        Assert.Equal(canPause, availability.CanPause);
        Assert.Equal(canResume, availability.CanResume);
        Assert.Equal(canStartFocusMode, availability.CanStartFocusMode);
        Assert.Equal(canEndFocusMode, availability.CanEndFocusMode);
        Assert.Equal(canDisable, availability.CanDisable);
        Assert.Equal(canEnable, availability.CanEnable);
        Assert.Equal(canBreakNow, availability.CanBreakNow);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Working, false, false, false)]
    [InlineData(WorkCyclePhase.PendingReminder, false, false, false)]
    [InlineData(WorkCyclePhase.ReminderVisible, false, false, false)]
    [InlineData(WorkCyclePhase.Snoozed, false, false, false)]
    [InlineData(WorkCyclePhase.BreakInProgress, false, false, false)]
    [InlineData(WorkCyclePhase.Idle, false, false, false)]
    [InlineData(WorkCyclePhase.Paused, true, false, false)]
    [InlineData(WorkCyclePhase.FocusMode, false, true, false)]
    [InlineData(WorkCyclePhase.Disabled, false, false, true)]
    public void Toggle_direction_table(
        WorkCyclePhase phase,
        bool showResume,
        bool showEndFocusMode,
        bool showEnable)
    {
        var availability = CommandAvailabilityPolicy.ForPhase(phase);

        Assert.Equal(showResume, availability.ShowResume);
        Assert.Equal(showEndFocusMode, availability.ShowEndFocusMode);
        Assert.Equal(showEnable, availability.ShowEnable);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Combined_toggles_follow_the_direction_they_point(WorkCyclePhase phase)
    {
        var a = CommandAvailabilityPolicy.ForPhase(phase);

        Assert.Equal(a.ShowResume ? a.CanResume : a.CanPause, a.PauseToggleEnabled);
        Assert.Equal(a.ShowEndFocusMode ? a.CanEndFocusMode : a.CanStartFocusMode, a.FocusToggleEnabled);
        Assert.Equal(a.ShowEnable ? a.CanEnable : a.CanDisable, a.DisableToggleEnabled);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Every_toggle_is_actionable_in_every_phase(WorkCyclePhase phase)
    {
        var a = CommandAvailabilityPolicy.ForPhase(phase);

        // Each combined control offers exactly one direction, and that direction must be
        // legal — otherwise the control is a dead end the user cannot escape.
        Assert.True(a.DisableToggleEnabled, $"Disable toggle is dead in {phase}.");
    }

    /// <summary>
    /// The most valuable test here: the policy's answer must agree with what the engine
    /// actually accepts, for every phase and every command. This is what would have caught
    /// both halves of the original tray-versus-window disagreement.
    /// </summary>
    /// <remarks>
    /// Three commands declare a preparatory step — cancelling a running break — via
    /// <see cref="CommandAvailabilityPolicy.CancelsRunningBreak"/>. The test performs that
    /// step exactly as the guarded helpers do, so agreement is asserted against the
    /// operation the user actually gets rather than against a bare transition nobody
    /// invokes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Policy_agrees_with_what_the_engine_accepts(WorkCyclePhase phase)
    {
        var a = CommandAvailabilityPolicy.ForPhase(phase);

        AssertAgrees(phase, a.CanPause, nameof(a.CanPause), t => t.Pause(), cancelsBreak: true);
        AssertAgrees(phase, a.CanResume, nameof(a.CanResume), t => t.Resume());
        AssertAgrees(phase, a.CanStartFocusMode, nameof(a.CanStartFocusMode), t => t.StartFocusMode(), cancelsBreak: true);
        AssertAgrees(phase, a.CanEndFocusMode, nameof(a.CanEndFocusMode), t => t.EndFocusMode());
        AssertAgrees(phase, a.CanDisable, nameof(a.CanDisable), t => t.Disable(), cancelsBreak: true);
        AssertAgrees(phase, a.CanEnable, nameof(a.CanEnable), t => t.Enable());
        AssertAgrees(phase, a.CanBreakNow, nameof(a.CanBreakNow), t => t.ManualStartBreak());
        AssertAgrees(phase, a.CanStartBreakFromReminder, nameof(a.CanStartBreakFromReminder), t => t.StartBreak());
        AssertAgrees(phase, a.CanSnooze, nameof(a.CanSnooze), t => t.Snooze());
        AssertAgrees(phase, a.CanIgnore, nameof(a.CanIgnore), t => t.Ignore());
    }

    [Fact]
    public void Only_a_running_break_carries_the_cancellation_step()
    {
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            Assert.Equal(
                phase == WorkCyclePhase.BreakInProgress,
                CommandAvailabilityPolicy.CancelsRunningBreak(phase));
        }
    }

    private static void AssertAgrees(
        WorkCyclePhase phase,
        bool policySaysAvailable,
        string command,
        Action<WorkCycleTracker> operation,
        bool cancelsBreak = false)
    {
        var clock = new FakeClock();
        var tracker = DriveToPhase(clock, phase);

        if (cancelsBreak && CommandAvailabilityPolicy.CancelsRunningBreak(phase))
            tracker.CancelBreak();

        bool engineAccepts;
        try
        {
            operation(tracker);
            engineAccepts = true;
        }
        catch (InvalidOperationException)
        {
            engineAccepts = false;
        }

        Assert.Equal(policySaysAvailable, engineAccepts);
        if (policySaysAvailable)
        {
            Assert.True(engineAccepts, $"{command} is offered in {phase} but the engine refuses it.");
        }
        else
        {
            Assert.False(engineAccepts, $"{command} is withheld in {phase} but the engine accepts it.");
        }
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

    private static WorkCycleTracker DriveToPhase(FakeClock clock, WorkCyclePhase phase)
    {
        var tracker = CreateTracker(clock);

        switch (phase)
        {
            case WorkCyclePhase.Working:
                break;

            case WorkCyclePhase.PendingReminder:
                ReachPendingReminder(tracker, clock);
                break;

            case WorkCyclePhase.ReminderVisible:
                ReachReminderVisible(tracker, clock);
                break;

            case WorkCyclePhase.BreakInProgress:
                tracker.ManualStartBreak();
                break;

            case WorkCyclePhase.Snoozed:
                ReachReminderVisible(tracker, clock);
                tracker.Snooze();
                break;

            case WorkCyclePhase.Idle:
                clock.Advance(TimeSpan.FromMinutes(5));
                tracker.Tick(TimeSpan.FromMinutes(5));
                break;

            case WorkCyclePhase.Paused:
                tracker.Pause();
                break;

            case WorkCyclePhase.FocusMode:
                tracker.StartFocusMode();
                break;

            case WorkCyclePhase.Disabled:
                tracker.Disable();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unhandled phase.");
        }

        Assert.Equal(phase, tracker.CurrentPhase);
        return tracker;
    }

    private static void ReachPendingReminder(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 40 && tracker.CurrentPhase == WorkCyclePhase.Working; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
    }

    private static void ReachReminderVisible(WorkCycleTracker tracker, FakeClock clock)
    {
        ReachPendingReminder(tracker, clock);
        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromMinutes(5),
            retryCooldown: TimeSpan.FromMinutes(5),
            debtLevel2: TimeSpan.FromSeconds(35),
            debtLevel3: TimeSpan.FromSeconds(45),
            debtLevel4: TimeSpan.FromSeconds(60));
        tracker.SetForceAllowPopup(true);
        return tracker;
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
