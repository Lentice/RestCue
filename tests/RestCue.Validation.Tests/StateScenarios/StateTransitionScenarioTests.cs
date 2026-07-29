using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class StateTransitionScenarioTests
{
    private static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultNaturalPause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultPassiveBreak = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultSnoozeDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultReminderDisplay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultRetryCooldown = TimeSpan.FromMinutes(20);

    [Fact]
    public void Starts_in_Working_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Working_to_PendingReminder_after_work_interval()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
    }

    [Fact]
    public void Working_to_Idle_when_idle_detected()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, idleThreshold: TimeSpan.FromSeconds(10), passiveBreak: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_to_Working_on_HandleUnlock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, idleThreshold: TimeSpan.FromSeconds(10), passiveBreak: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.FromSeconds(10));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void PendingReminder_to_ReminderVisible_on_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_to_BreakInProgress_on_StartBreak()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.StartBreak();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakInProgress_to_Working_after_break_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5), breakDuration: TimeSpan.FromSeconds(20));
        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakInProgress_to_Working_on_CancelBreak()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        tracker.CancelBreak();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_to_Snoozed_on_Snooze()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snoozed_to_PendingReminder_after_snooze_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5), snoozeDuration: TimeSpan.FromSeconds(10));
        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_Ignored_returns_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_and_Resume_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Pause();
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);

        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void FocusMode_start_and_end()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.StartFocusMode();
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_and_Enable_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Disable();
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);

        tracker.Enable();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleLock_and_HandleUnlock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.HandleLock();

        clock.Advance(TimeSpan.FromHours(1));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Sleep_does_not_accumulate_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleSleep();

        clock.Advance(TimeSpan.FromHours(2));
        tracker.Tick(TimeSpan.Zero);

        var timeBeforeResume = tracker.AccumulatedWorkTime;
        tracker.HandleResume();

        Assert.Equal(timeBeforeResume, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void TickActivityUnavailable_does_not_change_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_enters_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.ManualStartBreak();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    private static void ReachPendingReminder(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 31; i++)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    private static void ReachReminderVisible(WorkCycleTracker tracker, FakeClock clock)
    {
        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock clock,
        TimeSpan? workInterval = null,
        TimeSpan? idleThreshold = null,
        TimeSpan? naturalPause = null,
        TimeSpan? maxWait = null,
        TimeSpan? breakDuration = null,
        TimeSpan? passiveBreak = null,
        TimeSpan? snoozeDuration = null,
        TimeSpan? reminderDisplay = null,
        TimeSpan? retryCooldown = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval ?? TimeSpan.FromSeconds(30),
            idleThreshold ?? DefaultIdleThreshold,
            naturalPause ?? DefaultNaturalPause,
            maxWait ?? DefaultMaxWait,
            breakDuration ?? DefaultBreakDuration,
            passiveBreak ?? DefaultPassiveBreak,
            snoozeDuration ?? DefaultSnoozeDuration,
            reminderDisplay ?? DefaultReminderDisplay,
            retryCooldown ?? DefaultRetryCooldown);
        tracker.SetForceAllowPopup(true);
        return tracker;
    }
}
