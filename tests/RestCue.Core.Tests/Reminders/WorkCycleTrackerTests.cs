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

    [Fact]
    public void Constructor_throws_for_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkCycleTracker(
            null!, DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_throws_for_non_positive_workInterval(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), TimeSpan.FromSeconds(seconds), DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration));
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
    public void Skips_gap_when_resuming_after_idle()
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

        Assert.Equal(TimeSpan.FromSeconds(5), tracker.AccumulatedWorkTime);
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
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
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
    public void Passive_break_in_PendingReminder_transitions_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(21));
        tracker.Tick(TimeSpan.FromSeconds(21));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_break_in_ReminderVisible_transitions_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);
        clock.Advance(TimeSpan.FromSeconds(25));

        var passiveCompleted = false;
        tracker.PassiveBreakCompleted += (_, _) => passiveCompleted = true;

        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.True(passiveCompleted);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_break_in_PendingReminder_fires_separate_event()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int passiveFired = 0;
        int breakFired = 0;
        tracker.PassiveBreakCompleted += (_, _) => passiveFired++;
        tracker.BreakCompleted += (_, _) => breakFired++;

        clock.Advance(TimeSpan.FromSeconds(21));
        tracker.Tick(TimeSpan.FromSeconds(21));

        Assert.Equal(1, passiveFired);
        Assert.Equal(0, breakFired);
    }

    [Fact]
    public void Passive_break_does_not_trigger_below_threshold_in_Pending()
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
    public void Passive_break_does_not_trigger_in_Working_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_break_does_not_trigger_in_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            breakDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        int passiveFired = 0;
        tracker.PassiveBreakCompleted += (_, _) => passiveFired++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(0, passiveFired);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_break_at_exact_threshold_in_Pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.FromSeconds(20));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_break_just_below_threshold_does_not_transition()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(60),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(1));
        tracker.Tick(TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(1));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Passive_break_in_ReminderVisible_closes_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Natural_pause_does_not_trigger_when_passive_break_threshold_met_first()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            naturalPause: TimeSpan.FromSeconds(3),
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Constructor_throws_for_non_positive_passiveBreak()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, TimeSpan.Zero,
            DefaultSnoozeDuration, DefaultReminderDisplayDuration));
    }

    [Fact]
    public void Constructor_throws_for_non_positive_snoozeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            TimeSpan.Zero, DefaultReminderDisplayDuration));
    }

    [Fact]
    public void Constructor_throws_for_non_positive_reminderDisplayDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkCycleTracker(
            new FakeClock(), DefaultWorkInterval, DefaultIdleThreshold,
            DefaultNaturalPause, DefaultMaxWait, DefaultBreakDuration, DefaultPassiveBreak,
            DefaultSnoozeDuration, TimeSpan.Zero));
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
    public void Ignore_does_not_call_ResetCycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock);

        ReachReminderVisible(tracker, clock);

        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
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
    public void Passive_break_takes_priority_over_AutoDismissed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20),
            reminderDisplayDuration: TimeSpan.FromSeconds(30));

        ReachReminderVisible(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(35));
        tracker.Tick(TimeSpan.FromSeconds(35));

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Snooze_Ignore_AutoDismissed_are_mutually_exclusive()
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
        tracker.PassiveBreakCompleted += (_, _) => passiveBreakCount++;
        tracker.ReminderDismissed += (_, _) => dismissCount++;

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, breakCompletedCount);
        Assert.Equal(0, passiveBreakCount);
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

    private static WorkCycleTracker CreateTracker(
        FakeClock? clock = null,
        TimeSpan? workInterval = null,
        TimeSpan? idleThreshold = null,
        TimeSpan? naturalPause = null,
        TimeSpan? maxWait = null,
        TimeSpan? breakDuration = null,
        TimeSpan? passiveBreak = null,
        TimeSpan? snoozeDuration = null,
        TimeSpan? reminderDisplayDuration = null)
    {
        return new WorkCycleTracker(
            clock ?? new FakeClock(),
            workInterval ?? DefaultWorkInterval,
            idleThreshold ?? DefaultIdleThreshold,
            naturalPause ?? DefaultNaturalPause,
            maxWait ?? DefaultMaxWait,
            breakDuration ?? DefaultBreakDuration,
            passiveBreak ?? DefaultPassiveBreak,
            snoozeDuration ?? DefaultSnoozeDuration,
            reminderDisplayDuration ?? DefaultReminderDisplayDuration);
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

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset _utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
