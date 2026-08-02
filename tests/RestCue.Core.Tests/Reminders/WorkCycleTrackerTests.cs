using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class WorkCycleTrackerTests
{
    private static readonly TimeSpan DefaultWorkInterval = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultNaturalPause = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultPassiveBreak = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultSnoozeDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultReminderDisplayDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultRetryCooldown = TimeSpan.FromSeconds(1);

    [Fact]
    public void Constructor_throws_for_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkCycleTracker(
            null!, DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_throws_for_non_positive_workInterval(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), TimeSpan.FromSeconds(seconds), DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Fact]
    public void Starts_in_Working_phase()
    {
        var tracker = CreateTracker();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Starts_with_zero_accumulated_time()
    {
        var tracker = CreateTracker();
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Tick_throws_for_negative_idle_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tracker.Tick(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Accumulates_work_time_when_working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(3), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Does_not_accumulate_when_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(DefaultIdleThreshold);

        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Working_transitions_to_Idle_at_exact_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(DefaultIdleThreshold);
        tracker.Tick(DefaultIdleThreshold);

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Resumes_a_fresh_cycle_after_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(DefaultIdleThreshold);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Transitions_to_PendingReminder_when_work_threshold_reached()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.FromSeconds(30), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Transitions_to_ReminderVisible_on_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Transitions_to_ReminderVisible_on_max_wait_exceeded()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Does_not_transition_on_pause_below_natural_threshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.FromSeconds(3));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Fires_ReminderShown_on_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5));

        int fired = 0;
        tracker.ReminderShown += (_, _) => fired++;

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Fires_ReminderShown_on_max_wait()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            maxWait: TimeSpan.FromMinutes(3));

        int fired = 0;
        tracker.ReminderShown += (_, _) => fired++;

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Transitions_on_natural_pause_at_exact_threshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(5));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Does_not_transition_on_pause_just_below_natural_threshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        tracker.Tick(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Does_not_transition_on_max_wait_just_below()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(3) - TimeSpan.FromMilliseconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void StartBreak_transitions_to_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        tracker.StartBreak();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void StartBreak_throws_when_not_in_ReminderVisible()
    {
        var tracker = CreateTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.StartBreak());
    }

    [Fact]
    public void Break_completes_after_duration_and_resets()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            breakDuration: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Break_completes_fires_BreakCompleted_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            breakDuration: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        int fired = 0;
        tracker.BreakCompleted += (_, _) => fired++;

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Full_cycle_work_pending_visible_break_complete_reset()
    {
        var clock = new FakeClock();
        const int workIntervalSec = 30;
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(workIntervalSec),
            naturalPause: TimeSpan.FromSeconds(3),
            maxWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(5));

        int reminderShownCount = 0;
        int breakCompletedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.BreakCompleted += (_, _) => breakCompletedCount++;

        for (int i = 0; i < workIntervalSec + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);

        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, breakCompletedCount);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void PendingReminder_does_not_self_transition_on_working_ticks()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5),
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Unavailable_tick_in_Working_does_not_accumulate()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(60));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);

        var before = tracker.AccumulatedWorkTime;

        tracker.TickActivityUnavailable();

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Unavailable_tick_in_Pending_does_not_trigger_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5),
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Unavailable_tick_in_Pending_triggers_at_exact_max_wait()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Unavailable_tick_in_BreakInProgress_completes_and_resets()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            breakDuration: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        int fired = 0;
        tracker.BreakCompleted += (_, _) => fired++;

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Passive_pause_in_PendingReminder_preserves_phase_and_debt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(21));
        tracker.Tick(TimeSpan.FromSeconds(21));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_pause_in_ReminderVisible_hides_reminder_and_preserves_debt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        clock.Advance(TimeSpan.FromSeconds(25));

        var pauseDetected = false;
        tracker.PassivePauseDetected += (_, _) => pauseDetected = true;

        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.True(pauseDetected);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_pause_in_PendingReminder_fires_PassivePauseDetected_not_BreakCompleted()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int pauseFired = 0;
        int breakFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;
        tracker.BreakCompleted += (_, _) => breakFired++;

        clock.Advance(TimeSpan.FromSeconds(21));
        tracker.Tick(TimeSpan.FromSeconds(21));

        Assert.Equal(1, pauseFired);
        Assert.Equal(0, breakFired);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_does_not_trigger_below_threshold_in_Pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(60),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(19));
        tracker.Tick(TimeSpan.FromSeconds(19));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_does_not_trigger_in_Working_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_does_not_trigger_in_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            breakDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(0, pauseFired);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_at_exact_threshold_in_Pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.FromSeconds(20));

        Assert.Equal(1, pauseFired);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_pause_just_below_threshold_does_not_fire()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(60),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(1));
        tracker.Tick(TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, pauseFired);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_in_ReminderVisible_returns_to_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(1, pauseFired);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Natural_pause_does_not_trigger_when_passive_pause_threshold_met_first()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(3),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(1, pauseFired);
        Assert.Equal(0, reminderShown);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Constructor_throws_for_non_positive_passiveBreak()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, TimeSpan.Zero,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Fact]
    public void Constructor_throws_for_non_positive_snoozeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            TimeSpan.Zero, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Fact]
    public void Constructor_throws_for_non_positive_reminderDisplayDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, TimeSpan.Zero, DefaultWorkInterval));
    }

    [Fact]
    public void Constructor_throws_when_passiveBreakThreshold_equals_idleThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultIdleThreshold,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Fact]
    public void Constructor_throws_when_passiveBreakThreshold_exceeds_idleThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration,
            DefaultIdleThreshold + TimeSpan.FromSeconds(1),
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval));
    }

    [Fact]
    public void Constructor_accepts_passiveBreakThreshold_below_idleThreshold()
    {
        var tracker = new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration,
            TimeSpan.FromSeconds(20),
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultWorkInterval);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Constructor_throws_when_debtLevel2_not_greater_than_workInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultRetryCooldown,
            debtLevel2: DefaultWorkInterval));
    }

    [Fact]
    public void Ignore_transitions_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Ignore_throws_when_not_in_ReminderVisible()
    {
        var tracker = CreateTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.Ignore());
    }

    [Fact]
    public void Ignore_fires_ReminderDismissed_with_Ignored()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        ReminderResult? result = null;
        tracker.ReminderDismissed += (_, args) => result = args.Result;

        tracker.Ignore();

        Assert.Equal(ReminderResult.Ignored, result);
    }

    [Fact]
    public void Ignore_preserves_nonzero_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        var before = tracker.AccumulatedWorkTime;

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snooze_preserves_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        var before = tracker.AccumulatedWorkTime;

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snooze_transitions_to_Snoozed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snooze_throws_when_not_in_ReminderVisible()
    {
        var tracker = CreateTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.Snooze());
    }

    [Fact]
    public void Snooze_fires_ReminderDismissed_with_Snoozed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        ReminderResult? result = null;
        tracker.ReminderDismissed += (_, args) => result = args.Result;

        tracker.Snooze();

        Assert.Equal(ReminderResult.Snoozed, result);
    }

    [Fact]
    public void Snooze_suppresses_reminder_during_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(4));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snooze_expires_after_duration_returns_to_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Snooze_expires_in_TickActivityUnavailable()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void AutoDismissed_preserves_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        var before = tracker.AccumulatedWorkTime;

        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(before + TimeSpan.FromSeconds(30), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snooze_restores_exactly_one_reminder_flow()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void AutoDismissed_transitions_after_reminderDisplayDuration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void AutoDismissed_fires_ReminderDismissed_with_AutoDismissed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        ReminderResult? result = null;
        tracker.ReminderDismissed += (_, args) => result = args.Result;

        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(ReminderResult.AutoDismissed, result);
    }

    [Fact]
    public void AutoDismissed_does_not_fire_before_reminderDisplayDuration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(29));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void AutoDismissed_in_TickActivityUnavailable()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_takes_priority_over_AutoDismissed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(35));
        tracker.Tick(TimeSpan.FromSeconds(35));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Ignore_mutual_exclusion()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        int breakCompletedCount = 0;
        int passiveBreakCount = 0;
        int dismissCount = 0;
        tracker.BreakCompleted += (_, _) => breakCompletedCount++;
        tracker.PassivePauseDetected += (_, _) => passiveBreakCount++;
        tracker.ReminderDismissed += (_, _) => dismissCount++;

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, breakCompletedCount);
        Assert.Equal(0, passiveBreakCount);
        Assert.Equal(1, dismissCount);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(1, dismissCount);
    }

    [Fact]
    public void Snooze_mutual_exclusion()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        int breakCompletedCount = 0;
        int passiveBreakCount = 0;
        int dismissCount = 0;
        int reminderShownCount = 0;
        tracker.BreakCompleted += (_, _) => breakCompletedCount++;
        tracker.PassivePauseDetected += (_, _) => passiveBreakCount++;
        tracker.ReminderDismissed += (_, _) => dismissCount++;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
        Assert.Equal(0, breakCompletedCount);
        Assert.Equal(0, passiveBreakCount);
        Assert.Equal(1, dismissCount);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(1, dismissCount);

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(1, dismissCount);
    }

    [Fact]
    public void AutoDismissed_mutual_exclusion()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        int breakCompletedCount = 0;
        int passiveBreakCount = 0;
        int dismissCount = 0;
        tracker.BreakCompleted += (_, _) => breakCompletedCount++;
        tracker.PassivePauseDetected += (_, _) => passiveBreakCount++;
        tracker.ReminderDismissed += (_, _) => dismissCount++;

        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, breakCompletedCount);
        Assert.Equal(0, passiveBreakCount);
        Assert.Equal(1, dismissCount);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(1, dismissCount);
    }

    [Fact]
    public void Snooze_does_not_reset_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
        Assert.NotEqual(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Repeated_Ignore_does_not_accumulate_work_time_reduction()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Ignore();

        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);
    }

    [Theory]
    [InlineData(WorkCyclePhase.PendingReminder)]
    [InlineData(WorkCyclePhase.ReminderVisible)]
    [InlineData(WorkCyclePhase.Snoozed)]
    public void Accumulates_work_time_in_non_break_phases(WorkCyclePhase phase)
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            maxWait: TimeSpan.FromSeconds(10),
            snoozeDuration: TimeSpan.FromMinutes(10),
            reminderDisplayDuration: TimeSpan.FromSeconds(60));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        if (phase >= WorkCyclePhase.ReminderVisible)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        }

        if (phase == WorkCyclePhase.Snoozed)
        {
            tracker.Snooze();
        }

        var before = tracker.AccumulatedWorkTime;

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(before + TimeSpan.FromSeconds(5), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Does_not_accumulate_work_time_in_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            maxWait: TimeSpan.FromSeconds(10),
            breakDuration: TimeSpan.FromSeconds(20));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        tracker.StartBreak();
        var before = tracker.AccumulatedWorkTime;

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Working)]
    [InlineData(WorkCyclePhase.PendingReminder)]
    [InlineData(WorkCyclePhase.ReminderVisible)]
    [InlineData(WorkCyclePhase.Snoozed)]
    public void Unavailable_gap_not_backfilled_after_recovery(WorkCyclePhase phase)
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            maxWait: TimeSpan.FromMinutes(10),
            snoozeDuration: TimeSpan.FromMinutes(10),
            reminderDisplayDuration: TimeSpan.FromSeconds(60));

        if (phase == WorkCyclePhase.Working)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(5));
            tracker.Tick(TimeSpan.Zero);
        }
        else
        {
            for (int i = 0; i < 31; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                tracker.Tick(TimeSpan.Zero);
            }

            if (phase >= WorkCyclePhase.ReminderVisible)
            {
                clock.Advance(TimeSpan.FromSeconds(6));
                tracker.Tick(TimeSpan.FromSeconds(6));
            }

            if (phase == WorkCyclePhase.Snoozed)
            {
                tracker.Snooze();
            }
        }

        var before = tracker.AccumulatedWorkTime;

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.TickActivityUnavailable();

        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(2));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(before + TimeSpan.FromSeconds(2), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snoozed_transitions_to_Idle_when_idle_exceeds_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snoozed_reset_accumulated_work_time_after_transition_to_Idle_at_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snoozed_does_not_transition_to_idle_below_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(1));
        tracker.Tick(TimeSpan.FromMinutes(1));

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snoozed_does_not_transition_to_idle_at_exact_idleThreshold_minus_one()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1));
        tracker.Tick(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1));

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snoozed_transitions_to_Idle_at_exact_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(2));
        tracker.Tick(TimeSpan.FromMinutes(2));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snoozed_idle_priority_over_snooze_expiration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.FromMinutes(5));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Snoozed_snooze_expires_before_idle_threshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(10));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_phase_transitions_to_Working_when_user_resumes()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_phase_TickActivityUnavailable_preserves_Idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_phase_recovers_to_Working_on_genuine_activity_Tick()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            snoozeDuration: TimeSpan.FromMinutes(5),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.TickActivityUnavailable();
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Ignore_preserves_accumulated_work_and_gate_blocks_immediate()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(20),
            retryCooldown: TimeSpan.FromSeconds(60));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        var before = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, before);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void AutoDismissed_preserves_accumulated_work_and_gate_blocks_immediate()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(20),
            reminderDisplayDuration: TimeSpan.FromSeconds(10),
            retryCooldown: TimeSpan.FromSeconds(60));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock? clock = null,
        TimeSpan? workInterval = null,
        TimeSpan? idleThreshold = null,
        TimeSpan? naturalPause = null,
        TimeSpan? maxWait = null,
        TimeSpan? breakDuration = null,
        TimeSpan? passiveBreak = null,
        TimeSpan? snoozeDuration = null,
        TimeSpan? reminderDisplayDuration = null,
        TimeSpan? retryCooldown = null)
    {
        var tracker = new WorkCycleTracker(
            clock ?? new FakeClock(),
            workInterval ?? DefaultWorkInterval,
            idleThreshold ?? DefaultIdleThreshold,
            naturalPause ?? DefaultNaturalPause,
            maxWait ?? DefaultMaxWait,
            breakDuration ?? DefaultBreakDuration,
            passiveBreak ?? DefaultPassiveBreak,
            snoozeDuration ?? DefaultSnoozeDuration,
            reminderDisplayDuration ?? DefaultReminderDisplayDuration,
            retryCooldown ?? DefaultRetryCooldown);
        tracker.SetForceAllowPopup(true);
        return tracker;
    }

    private static void ReachPendingReminder(
        WorkCycleTracker tracker, FakeClock clock, int maxTicks = 2000)
    {
        for (int i = 0; i < maxTicks && tracker.CurrentPhase == WorkCyclePhase.Working; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
    }

    private static void ReachReminderVisible(
        WorkCycleTracker tracker, FakeClock clock, int maxTicks = 2000)
    {
        ReachPendingReminder(tracker, clock, maxTicks);
        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void HandleLock_from_PendingReminder_clears_stale_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_ReminderVisible_clears_stale_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_Snoozed_clears_stale_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_Working_resets_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_BreakInProgress_preserves_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, breakDuration: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        var before = tracker.AccumulatedWorkTime;

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_FocusMode_resets_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_Working_blocks_accumulation_during_lock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.HandleLock();

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleLock_from_Working_blocks_reminder_during_lock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.HandleLock();

        for (int i = 0; i < 50; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(0, reminderShown);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_FocusMode_blocks_accumulation_during_lock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleLock();

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_FocusMode_HandleUnlock_starts_fresh()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleUnlock_resets_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleSleep_resets_cycle_independently()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);

        tracker.HandleSleep();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleResume_after_sleep_resets_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleSleep();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Sleep_then_lock_HandleResume_does_not_clear_lock_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleSleep();
        tracker.HandleLock();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Sleep_then_lock_HandleUnlock_does_not_clear_sleep_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleSleep();
        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Sleep_then_lock_HandleBoth_clears_all_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleSleep();
        tracker.HandleLock();
        tracker.HandleResume();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Lock_then_sleep_HandleResume_does_not_clear_lock_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleLock();
        tracker.HandleSleep();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Lock_then_sleep_HandleUnlock_does_not_clear_sleep_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleLock();
        tracker.HandleSleep();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Lock_then_sleep_HandleBoth_clears_all_suppression()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.HandleLock();
        tracker.HandleSleep();
        tracker.HandleUnlock();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleSleep_blocks_TickActivityUnavailable()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.HandleSleep();

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Unlock_after_lock_does_not_immediately_show_stale_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleLock_from_Paused_preserves_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Pause();

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleLock_from_Disabled_preserves_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Disable();

        tracker.HandleLock();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void HandleUnlock_from_Paused_preserves_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Pause();

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleUnlock_from_Disabled_preserves_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Disable();

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Paused_lock_unlock_legal_recovery_via_Resume()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        tracker.Pause();

        tracker.HandleLock();
        tracker.HandleUnlock();

        int resumed = 0;
        tracker.Resumed += (_, _) => resumed++;

        tracker.Resume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void Disabled_lock_unlock_legal_recovery_via_Enable()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Disable();

        tracker.HandleLock();
        tracker.HandleUnlock();

        int enabled = 0;
        tracker.Enabled += (_, _) => enabled++;

        tracker.Enable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, enabled);
    }

    [Fact]
    public void Paused_sleep_resume_no_unexpected_transition()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        var accumulated = tracker.AccumulatedWorkTime;
        tracker.Pause();

        tracker.HandleSleep();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(accumulated, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Disabled_sleep_resume_no_unexpected_transition()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Disable();

        tracker.HandleSleep();
        tracker.HandleResume();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleUnlock_from_Paused_fires_no_lifecycle_events()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause();

        int fired = 0;
        tracker.Paused += (_, _) => fired++;
        tracker.Resumed += (_, _) => fired++;
        tracker.Disabled += (_, _) => fired++;
        tracker.Enabled += (_, _) => fired++;
        tracker.ReminderShown += (_, _) => fired++;

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void HandleUnlock_from_Disabled_fires_no_lifecycle_events()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Disable();

        int fired = 0;
        tracker.Paused += (_, _) => fired++;
        tracker.Resumed += (_, _) => fired++;
        tracker.Disabled += (_, _) => fired++;
        tracker.Enabled += (_, _) => fired++;
        tracker.ReminderShown += (_, _) => fired++;

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Pause_transitions_from_Working()
    {
        var tracker = CreateTracker();
        tracker.Pause();
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_transitions_from_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);

        tracker.Pause();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_transitions_from_ReminderVisible()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.Pause();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_from_Snoozed_preserves_Need_and_abandons_snooze()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        var need = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, need);

        tracker.Snooze();
        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.Pause();
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(need, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(need, tracker.AccumulatedWorkTime);
        Assert.Equal(0, reminderShown);

        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(need, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.FocusMode)]
    [InlineData(WorkCyclePhase.Disabled)]
    [InlineData(WorkCyclePhase.BreakInProgress)]
    [InlineData(WorkCyclePhase.Idle)]
    public void Pause_throws_from_invalid_phases(WorkCyclePhase phase)
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        if (phase != WorkCyclePhase.Paused)
            ReachReminderVisible(tracker, clock);
        if (phase == WorkCyclePhase.BreakInProgress)
            tracker.StartBreak();

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                tracker.Pause();
                break;
            case WorkCyclePhase.FocusMode:
                tracker.StartFocusMode();
                break;
            case WorkCyclePhase.Disabled:
                tracker.Disable();
                break;
            case WorkCyclePhase.Idle:
                tracker.Snooze();
                clock.Advance(TimeSpan.FromMinutes(3));
                tracker.Tick(TimeSpan.FromMinutes(3));
                Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => tracker.Pause());
    }

    [Fact]
    public void Resume_transitions_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause();

        tracker.Resume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Resume_throws_when_not_paused()
    {
        var tracker = CreateTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.Resume());
    }

    [Fact]
    public void Pause_fires_Paused_event()
    {
        var tracker = CreateTracker();
        int fired = 0;
        tracker.Paused += (_, _) => fired++;

        tracker.Pause();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Resume_fires_Resumed_event()
    {
        var tracker = CreateTracker();
        tracker.Pause();

        int fired = 0;
        tracker.Resumed += (_, _) => fired++;

        tracker.Resume();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void No_accumulation_while_paused()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        var before = tracker.AccumulatedWorkTime;

        tracker.Pause();

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Tick_in_Paused_does_not_transition()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause();

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void StartFocusMode_transitions_from_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Tick(TimeSpan.Zero);

        tracker.StartFocusMode();

        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
    }

    [Fact]
    public void StartFocusMode_transitions_from_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);

        tracker.StartFocusMode();

        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
    }

    [Fact]
    public void FocusMode_from_ReminderVisible_accumulates_and_ends_at_most_Pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        var needBefore = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, needBefore);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.StartFocusMode();
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(needBefore + TimeSpan.FromSeconds(10), tracker.AccumulatedWorkTime);
        Assert.Equal(0, reminderShown);

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Fact]
    public void FocusMode_from_Snoozed_abandons_snooze_and_accumulates()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        var needBefore = tracker.AccumulatedWorkTime;
        tracker.Snooze();
        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.StartFocusMode();
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(needBefore + TimeSpan.FromMinutes(10), tracker.AccumulatedWorkTime);
        Assert.Equal(0, reminderShown);

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Fact]
    public void Pause_clears_stale_suppressed_so_unsuppress_after_resume_uses_normal_timing()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(3),
            maxWait: TimeSpan.FromSeconds(60));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(true, true, false, null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        tracker.Pause();

        tracker.Resume();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.UpdateForegroundContext(false, false, false, null);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Fact]
    public void FocusMode_clears_stale_suppressed_so_unsuppress_after_end_uses_normal_timing()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(3),
            maxWait: TimeSpan.FromSeconds(60));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(true, true, false, null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        tracker.StartFocusMode();

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        tracker.UpdateForegroundContext(false, false, false, null);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.FocusMode)]
    [InlineData(WorkCyclePhase.Disabled)]
    [InlineData(WorkCyclePhase.BreakInProgress)]
    [InlineData(WorkCyclePhase.Idle)]
    public void StartFocusMode_throws_from_invalid_phases(WorkCyclePhase phase)
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                tracker.Pause();
                break;
            case WorkCyclePhase.FocusMode:
                tracker.StartFocusMode();
                break;
            case WorkCyclePhase.Disabled:
                tracker.Disable();
                break;
            case WorkCyclePhase.BreakInProgress:
                ReachReminderVisible(tracker, clock);
                tracker.StartBreak();
                break;
            case WorkCyclePhase.Idle:
                ReachReminderVisible(tracker, clock);
                tracker.Snooze();
                clock.Advance(TimeSpan.FromMinutes(3));
                tracker.Tick(TimeSpan.FromMinutes(3));
                Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => tracker.StartFocusMode());
    }

    [Fact]
    public void EndFocusMode_transitions_to_PendingReminder_when_threshold_exceeded()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void EndFocusMode_transitions_to_Working_when_threshold_not_exceeded()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        tracker.StartFocusMode();
        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void EndFocusMode_fires_at_most_one_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(20));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();

        for (int i = 0; i < 61; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Work_accumulates_during_FocusMode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(20), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void FocusMode_at_Idle_threshold_enters_Idle_and_resets_Need()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            idleThreshold: TimeSpan.FromSeconds(10),
            passiveBreak: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void EndFocusMode_at_exact_work_interval_enters_one_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();
        tracker.Tick(TimeSpan.Zero);
        for (int i = 0; i < 30; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.EndFocusMode();

        Assert.Equal(TimeSpan.FromSeconds(30), tracker.AccumulatedWorkTime);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);
    }

    [Fact]
    public void EndFocusMode_does_not_transition_to_ReminderVisible_directly()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.StartFocusMode();

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);
    }

    [Fact]
    public void StartFocusMode_fires_FocusModeStarted_event()
    {
        var tracker = CreateTracker();
        int fired = 0;
        tracker.FocusModeStarted += (_, _) => fired++;

        tracker.StartFocusMode();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void EndFocusMode_fires_FocusModeEnded_event()
    {
        var tracker = CreateTracker();
        tracker.StartFocusMode();

        int fired = 0;
        tracker.FocusModeEnded += (_, _) => fired++;

        tracker.EndFocusMode();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Disable_transitions_to_Disabled()
    {
        var tracker = CreateTracker();
        tracker.Disable();
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_throws_when_already_disabled()
    {
        var tracker = CreateTracker();
        tracker.Disable();
        Assert.Throws<InvalidOperationException>(() => tracker.Disable());
    }

    [Fact]
    public void Enable_transitions_to_Working()
    {
        var tracker = CreateTracker();
        tracker.Disable();

        tracker.Enable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Enable_throws_when_not_disabled()
    {
        var tracker = CreateTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.Enable());
    }

    [Fact]
    public void No_accumulation_while_disabled()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        var before = tracker.AccumulatedWorkTime;

        tracker.Disable();

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Tick_in_Disabled_does_not_transition()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Disable();

        clock.Advance(TimeSpan.FromMinutes(30));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_fires_Disabled_event()
    {
        var tracker = CreateTracker();
        int fired = 0;
        tracker.Disabled += (_, _) => fired++;

        tracker.Disable();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Enable_fires_Enabled_event()
    {
        var tracker = CreateTracker();
        tracker.Disable();

        int fired = 0;
        tracker.Enabled += (_, _) => fired++;

        tracker.Enable();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Disable_from_PendingReminder_clears_reminder_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);

        tracker.Disable();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_from_ReminderVisible_clears_reminder_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);

        tracker.Disable();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_preserves_debt_level_Enable_resets_to_Level0_with_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        tracker.Disable();
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        int fired = 0;
        RestDebtLevel previous = RestDebtLevel.Level0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            previous = args.Previous;
            fired++;
        };

        tracker.Enable();
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
        Assert.Equal(1, fired);
        Assert.Equal(RestDebtLevel.Level1, previous);
    }

    [Fact]
    public void Resume_preserves_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 25; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        var before = tracker.AccumulatedWorkTime;

        tracker.Pause();
        tracker.Resume();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Resume_preserves_unexpired_cooldown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        var before = tracker.CooldownUntil;

        tracker.Pause();
        tracker.Resume();

        Assert.Equal(before, tracker.CooldownUntil);
    }

    [Fact]
    public void Resume_preserves_nextDebtDeadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        tracker.Ignore();
        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(15));

        tracker.Pause();
        tracker.Resume();

        Assert.NotNull(tracker.CooldownUntil);
    }

    [Fact]
    public void Resume_cooldown_expired_during_pause_allows_tick_reevaluation()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);
        var beforeWork = tracker.AccumulatedWorkTime;

        tracker.Pause();

        clock.Advance(TimeSpan.FromSeconds(15));

        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(beforeWork, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void TickActivityUnavailable_in_Paused_preserves_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_rejects_negative_duration()
    {
        var tracker = CreateTracker();

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Pause(TimeSpan.FromSeconds(-1)));
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void TickActivityUnavailable_timed_pause_expires_after_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        var duration = TimeSpan.FromSeconds(10);
        tracker.Pause(duration);

        int resumed = 0;
        tracker.Resumed += (_, _) => resumed++;

        clock.Advance(TimeSpan.FromSeconds(11));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void TickActivityUnavailable_timed_pause_stays_paused_before_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause(TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(9));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void TickActivityUnavailable_timed_pause_expires_at_exact_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause(TimeSpan.FromSeconds(10));

        int resumed = 0;
        tracker.Resumed += (_, _) => resumed++;

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void Tick_timed_pause_expires_after_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        var duration = TimeSpan.FromSeconds(10);
        tracker.Pause(duration);

        int resumed = 0;
        tracker.Resumed += (_, _) => resumed++;

        clock.Advance(TimeSpan.FromSeconds(11));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void Tick_timed_pause_stays_paused_before_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause(TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(9));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Tick_timed_pause_expires_at_exact_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Pause(TimeSpan.FromSeconds(10));

        int resumed = 0;
        tracker.Resumed += (_, _) => resumed++;

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void TickActivityUnavailable_in_FocusMode_preserves_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.StartFocusMode();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
    }

    [Fact]
    public void TickActivityUnavailable_in_Disabled_preserves_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.Disable();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_pause_preserves_debt_across_multiple_ticks()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int pauseDetected = 0;
        tracker.PassivePauseDetected += (_, _) => pauseDetected++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(1, pauseDetected);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(30));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(1, pauseDetected);
    }

    [Fact]
    public void Passive_pause_work_resumes_after_user_returns()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            workInterval: TimeSpan.FromSeconds(60));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        var afterPause = tracker.AccumulatedWorkTime;

        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.True(tracker.AccumulatedWorkTime > afterPause);
    }

    [Fact]
    public void Passive_pause_in_ReminderVisible_next_natural_pause_shows_reminder_again()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(5),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Fact]
    public void PendingReminder_transitions_to_Idle_when_idle_exceeds_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachPendingReminder(tracker, clock);

        int breakCompleted = 0;
        int pauseDetected = 0;
        tracker.BreakCompleted += (_, _) => breakCompleted++;
        tracker.PassivePauseDetected += (_, _) => pauseDetected++;

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(0, breakCompleted);
        Assert.Equal(0, pauseDetected);
    }

    [Fact]
    public void ReminderVisible_transitions_to_Idle_when_idle_exceeds_idleThreshold()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            idleThreshold: TimeSpan.FromMinutes(2));

        ReachReminderVisible(tracker, clock);

        int breakCompleted = 0;
        int pauseDetected = 0;
        tracker.BreakCompleted += (_, _) => breakCompleted++;
        tracker.PassivePauseDetected += (_, _) => pauseDetected++;

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(0, breakCompleted);
        Assert.Equal(0, pauseDetected);
    }

    [Fact]
    public void Passive_pause_recovery_does_not_show_reminder_immediately()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(1),
            passiveBreak: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(60),
            maxWait: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromSeconds(30));

        ReachPendingReminder(tracker, clock);

        int reminderShown = 0;
        int pauseFired = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(15));
        tracker.Tick(TimeSpan.FromSeconds(15));
        Assert.Equal(1, pauseFired);
        Assert.Equal(0, reminderShown);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Ignore_does_not_reset_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromSeconds(300));

        ReachReminderVisible(tracker, clock);

        tracker.Ignore();

        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void AutoDismissed_does_not_reset_AccumulatedWorkTime()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            reminderDisplayDuration: TimeSpan.FromSeconds(10),
            retryCooldown: TimeSpan.FromSeconds(300));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Cooldown_suppresses_reminder_during_retry_cooldown_period()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(10),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        _ = clock.Elapsed;

        clock.Advance(TimeSpan.FromSeconds(15));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.True(tracker.CooldownUntil.HasValue);
        Assert.True(tracker.CooldownUntil.Value > clock.Elapsed);
    }

    [Fact]
    public void Cooldown_expires_and_allows_reminder_flow()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(10),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 15; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Supplied_debt_deadline_earlier_than_cooldown_triggers_flow_at_debt_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(15));

        for (int i = 0; i < 14; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Supplied_debt_deadline_later_than_cooldown_cooldown_is_effective_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(30));

        for (int i = 0; i < 9; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Need_is_total_accumulated_work_not_reset_by_reminder_show()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void FocusMode_preserves_cooldown_deadline_and_normal_reevaluation_after_end()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.StartFocusMode();
        Assert.NotNull(tracker.CooldownUntil);

        for (int i = 0; i < 15; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void EndFocusMode_before_cooldown_expiry_stays_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.StartFocusMode();

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.NotNull(tracker.CooldownUntil);
    }

    [Fact]
    public void EndFocusMode_at_exact_cooldown_deadline_enters_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.StartFocusMode();

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void FocusMode_preserves_supplied_debt_deadline_and_triggers_after_end()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(15));
        tracker.StartFocusMode();

        for (int i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        tracker.EndFocusMode();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_preserves_cooldown_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        var before = tracker.CooldownUntil;

        tracker.Pause();
        Assert.NotNull(tracker.CooldownUntil);
        Assert.Equal(before, tracker.CooldownUntil);

        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Retry_path_TrayOnly_suppresses_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(3),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(false, true, true, null);

        int suppressed = 0;
        bool? showTrayCue = null;
        tracker.ReminderSuppressed += (_, e) =>
        {
            suppressed++;
            showTrayCue = e.ShowTrayCue;
        };

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(1, suppressed);
        Assert.True(showTrayCue);
    }

    [Fact]
    public void Custom_longer_interval_during_cooldown_prevents_flow_until_interval_due()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.UpdateForegroundContext(false, false, false, TimeSpan.FromSeconds(60));

        for (int i = 0; i < 49; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Retry_path_max_wait_triggers_reminder_during_continuous_input()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(60),
            maxWait: TimeSpan.FromSeconds(3),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Retry_path_fullscreen_suppresses_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            naturalPause: TimeSpan.FromSeconds(3),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(true, false, false, null);

        int suppressed = 0;
        tracker.ReminderSuppressed += (_, _) => suppressed++;

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(1, suppressed);
    }

    [Fact]
    public void Cooldown_exact_deadline_before_is_suppressed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 19; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.NotNull(tracker.CooldownUntil);
    }

    [Fact]
    public void Cooldown_exact_deadline_at_allows_flow()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Cooldown_exact_deadline_after_allows_flow()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Cooldown_does_not_block_re_evaluation_of_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(10),
            naturalPause: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Cooldown_on_auto_dismissed_suppresses_new_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(10),
            reminderDisplayDuration: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(15));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.True(tracker.CooldownUntil.HasValue);
    }

    [Fact]
    public void Cooldown_resets_on_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(10),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(DefaultIdleThreshold);

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Snooze_unaffected_by_retry_cooldown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5));

        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Constructor_throws_for_non_positive_retryCooldown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_throws_for_non_positive_FocusModeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultRetryCooldown,
            focusModeDuration: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_accepts_default_FocusModeDuration()
    {
        var tracker = new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration, DefaultRetryCooldown);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_accepts_deadline_and_it_blocks_until_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        var deadline = clock.Elapsed + TimeSpan.FromSeconds(10);
        tracker.SetNextDebtDeadline(deadline);

        for (int i = 0; i < 9; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_null_allows_cooldown_to_govern()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(null);

        for (int i = 0; i < 9; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_cleared_on_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(300));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(DefaultIdleThreshold);
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_before_Ignore_does_not_cause_early_retry()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(10));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        for (int i = 0; i < 15; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_during_cooldown_selects_earlier_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(15));

        for (int i = 0; i < 15; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void SetNextDebtDeadline_null_clears_supplied_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(10));
        tracker.SetNextDebtDeadline(null);

        for (int i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void StartBreakNow_race_safety_second_call_does_not_close_break()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        Assert.Throws<InvalidOperationException>(() => tracker.ManualStartBreak());
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_accepts_Working()
    {
        var tracker = CreateTracker();
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_accepts_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachPendingReminder(tracker, clock);
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_accepts_ReminderVisible()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_accepts_Snoozed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, snoozeDuration: TimeSpan.FromMinutes(5));
        ReachReminderVisible(tracker, clock);
        tracker.Snooze();
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_accepts_FocusMode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        tracker.StartFocusMode();
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_throws_from_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);
        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        Assert.Throws<InvalidOperationException>(() => tracker.ManualStartBreak());
    }

    [Fact]
    public void ManualStartBreak_throws_from_Disabled()
    {
        var tracker = CreateTracker();
        tracker.Disable();
        Assert.Throws<InvalidOperationException>(() => tracker.ManualStartBreak());
    }

    [Fact]
    public void ManualStartBreak_throws_from_Paused()
    {
        var tracker = CreateTracker();
        tracker.Pause();
        Assert.Throws<InvalidOperationException>(() => tracker.ManualStartBreak());
    }

    [Fact]
    public void ManualStartBreak_throws_from_Idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            idleThreshold: TimeSpan.FromSeconds(10),
            passiveBreak: TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(15));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Throws<InvalidOperationException>(() => tracker.ManualStartBreak());
    }

    [Fact]
    public void Supplied_debt_deadline_before_both_cooldown_and_work_interval_triggers_at_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(60),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        tracker.SetNextDebtDeadline(clock.Elapsed + TimeSpan.FromSeconds(10));

        for (int i = 0; i < 9; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Starts_at_debt_Level0()
    {
        var tracker = CreateTracker();
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_reaches_Level1_at_work_interval()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_fires_RestDebtLevelChanged_when_crossing_Level1()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        RestDebtLevel previous = RestDebtLevel.Level0;
        RestDebtLevel current = RestDebtLevel.Level0;
        int fired = 0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            previous = args.Previous;
            current = args.Current;
            fired++;
        };

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(1, fired);
        Assert.Equal(RestDebtLevel.Level0, previous);
        Assert.Equal(RestDebtLevel.Level1, current);
    }

    [Fact]
    public void Debt_does_not_fire_event_when_level_unchanged()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        ReachPendingReminder(tracker, clock);

        int fired = 0;
        tracker.RestDebtLevelChanged += (_, _) => fired++;

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Debt_resets_to_Level0_on_BreakCompleted_with_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            breakDuration: TimeSpan.FromSeconds(5));

        ReachReminderVisible(tracker, clock);

        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        int fired = 0;
        RestDebtLevel previous = RestDebtLevel.Level0;
        RestDebtLevel current = RestDebtLevel.Level0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            previous = args.Previous;
            current = args.Current;
            fired++;
        };

        tracker.StartBreak();
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
        Assert.Equal(1, fired);
        Assert.Equal(RestDebtLevel.Level1, previous);
        Assert.Equal(RestDebtLevel.Level0, current);
    }

    [Fact]
    public void Debt_resets_to_Level0_on_Idle_with_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachPendingReminder(tracker, clock);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        int fired = 0;
        RestDebtLevel previous = RestDebtLevel.Level0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            previous = args.Previous;
            fired++;
        };

        clock.Advance(DefaultIdleThreshold);
        tracker.Tick(DefaultIdleThreshold);

        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
        Assert.Equal(1, fired);
        Assert.Equal(RestDebtLevel.Level1, previous);
    }

    [Fact]
    public void Repeated_reset_at_Level0_emits_no_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);

        int fired = 0;
        tracker.RestDebtLevelChanged += (_, _) => fired++;

        clock.Advance(DefaultIdleThreshold);
        tracker.Tick(DefaultIdleThreshold);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
        Assert.Equal(0, fired);

        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Large_clock_jump_fires_one_event_from_previous_to_final()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            breakDuration: TimeSpan.FromSeconds(20));

        tracker.Tick(TimeSpan.Zero);

        int fired = 0;
        RestDebtLevel previous = RestDebtLevel.Level0;
        RestDebtLevel current = RestDebtLevel.Level0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            previous = args.Previous;
            current = args.Current;
            fired++;
        };

        clock.Advance(TimeSpan.FromMinutes(65));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, fired);
        Assert.Equal(RestDebtLevel.Level0, previous);
        Assert.Equal(RestDebtLevel.Level4, current);
        Assert.Equal(RestDebtLevel.Level4, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_reaches_Level2_at_exact_35_minutes()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            maxWait: TimeSpan.FromHours(2),
            retryCooldown: TimeSpan.FromHours(2));

        for (int i = 0; i < 35 * 60 + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level2, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_reaches_Level3_at_exact_45_minutes()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            maxWait: TimeSpan.FromHours(2),
            retryCooldown: TimeSpan.FromHours(2));

        for (int i = 0; i < 45 * 60 + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level3, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_sequential_Level0_to_Level1_to_Level2_fires_two_events()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            maxWait: TimeSpan.FromHours(2),
            retryCooldown: TimeSpan.FromHours(2));

        var events = new List<RestDebtLevelChangedEventArgs>();
        tracker.RestDebtLevelChanged += (_, args) => events.Add(args);

        for (int i = 0; i < 20 * 60 + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Single(events);
        Assert.Equal(RestDebtLevel.Level0, events[0].Previous);
        Assert.Equal(RestDebtLevel.Level1, events[0].Current);

        for (int i = 0; i < 15 * 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(2, events.Count);
        Assert.Equal(RestDebtLevel.Level1, events[1].Previous);
        Assert.Equal(RestDebtLevel.Level2, events[1].Current);
        Assert.Equal(RestDebtLevel.Level2, tracker.RestDebtLevel);
    }

    [Fact]
    public void Debt_deadline_triggers_reminder_when_crossing_level_during_cooldown()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromHours(1),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromHours(1),
            breakDuration: TimeSpan.FromMinutes(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromSeconds(30),
            debtLevel2: TimeSpan.FromSeconds(20),
            debtLevel3: TimeSpan.FromSeconds(25),
            debtLevel4: TimeSpan.FromHours(4));
        tracker.SetForceAllowPopup(true);

        for (int i = 0; i < 11; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        var cooldownBeforeIgnore = clock.Elapsed;
        tracker.Ignore();
        var cooldownUntil = cooldownBeforeIgnore + TimeSpan.FromSeconds(30);

        // 16s of work accumulated at Ignore; Level 2 is reached after 4 more seconds,
        // well inside the 30s cooldown.
        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(cooldownUntil, tracker.CooldownUntil);

        // This threshold re-evaluates, rather than the one after it. Ignore() ends the
        // current attempt and drops its accumulation bookkeeping, so one tick of work is
        // never credited and the wall-clock deadline lands one tick short of the 20s
        // threshold. Firing a tick early is harmless; firing a level late was the defect.
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
        Assert.True(clock.Elapsed < cooldownUntil,
            "The threshold deadline, not the cooldown expiry, must have driven the re-evaluation.");
    }

    [Fact]
    public void Debt_deadline_survives_unavailable_activity_during_cooldown()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromHours(1),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromHours(1),
            breakDuration: TimeSpan.FromMinutes(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromSeconds(30),
            debtLevel2: TimeSpan.FromSeconds(20),
            debtLevel3: TimeSpan.FromSeconds(25),
            debtLevel4: TimeSpan.FromHours(4));
        tracker.SetForceAllowPopup(true);

        for (int i = 0; i < 11; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        var cooldownUntil = clock.Elapsed + TimeSpan.FromSeconds(30);
        tracker.Ignore();

        // Activity is unavailable across the armed threshold deadline: nothing
        // accumulates and nothing re-evaluates, but the deadline is not lost.
        for (int i = 0; i < 6; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.TickActivityUnavailable();
        }
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(cooldownUntil, tracker.CooldownUntil);

        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Debt_reaches_Level4_at_exact_60_minutes()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            maxWait: TimeSpan.FromHours(2),
            retryCooldown: TimeSpan.FromHours(2));

        for (int i = 0; i < 60 * 60 + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(RestDebtLevel.Level4, tracker.RestDebtLevel);
    }

    [Fact]
    public void Wall_clock_regression_does_not_regress_debt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        var savedLevel = tracker.RestDebtLevel;
        var savedAccum = tracker.AccumulatedWorkTime;
        Assert.Equal(RestDebtLevel.Level1, savedLevel);
        Assert.NotEqual(TimeSpan.Zero, savedAccum);

        clock.Advance(TimeSpan.FromSeconds(-10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(savedLevel, tracker.RestDebtLevel);
        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void CancelBreak_preserves_accumulated_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            naturalPause: TimeSpan.FromSeconds(5),
            maxWait: TimeSpan.FromMinutes(3));
        int cancelled = 0;
        var savedLevel = RestDebtLevel.Level0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        var savedAccum = tracker.AccumulatedWorkTime;
        savedLevel = tracker.RestDebtLevel;
        Assert.NotEqual(TimeSpan.Zero, savedAccum);

        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        tracker.CancelBreak();

        Assert.Equal(1, cancelled);
        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);
        Assert.Equal(savedLevel, tracker.RestDebtLevel);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void CancelBreak_outside_break_is_noop()
    {
        var tracker = CreateTracker();
        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        tracker.CancelBreak();

        Assert.Equal(0, cancelled);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void CancelBreak_does_not_credit_break_duration_as_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        var savedAccum = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, savedAccum);

        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(15));
        tracker.CancelBreak();

        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(2));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(savedAccum + TimeSpan.FromSeconds(2), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void CancelBreak_with_active_cooldown_returns_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(5),
            retryCooldown: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);
        var savedAccum = tracker.AccumulatedWorkTime;

        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.CancelBreak();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);
        Assert.NotNull(tracker.CooldownUntil);
    }

    [Fact]
    public void CancelBreak_with_active_cooldown_but_debt_deadline_passed_enters_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromHours(1),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromHours(1),
            breakDuration: TimeSpan.FromMinutes(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(30),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromSeconds(30),
            debtLevel2: TimeSpan.FromSeconds(15),
            debtLevel3: TimeSpan.FromSeconds(20),
            debtLevel4: TimeSpan.FromHours(4));
        tracker.SetForceAllowPopup(true);

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.CancelBreak();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void CancelBreak_with_no_cooldown_enters_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        var savedAccum = tracker.AccumulatedWorkTime;

        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        tracker.CancelBreak();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(savedAccum, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Break_completion_still_resets_need()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30),
            naturalPause: TimeSpan.FromSeconds(5),
            maxWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(20));
        int completed = 0;
        tracker.BreakCompleted += (_, _) => completed++;

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        var savedAccum = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, savedAccum);

        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, completed);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private TimeSpan elapsed;

        public DateTimeOffset UtcNow => utcNow;

        public TimeSpan Elapsed => elapsed;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
            elapsed += duration;
        }
    }
}
