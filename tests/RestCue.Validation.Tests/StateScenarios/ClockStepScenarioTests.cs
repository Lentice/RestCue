using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Validation.Tests.StateScenarios;

/// <summary>
/// Elapsed time is measured monotonically, so a step in civil time — a manual clock
/// edit, a virtual-machine restore, a bad-RTC first-boot correction — must move no
/// deadline the app owns. Every test here steps the wall clock without advancing
/// elapsed time, or the reverse, which only <see cref="FakeClock"/> can express.
/// </summary>
public sealed class ClockStepScenarioTests
{
    private static readonly TimeSpan Step = TimeSpan.FromHours(1);

    [Fact]
    public void Forward_clock_step_does_not_complete_a_break_early()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, breakDuration: TimeSpan.FromSeconds(20));
        int completed = 0;
        tracker.BreakCompleted += (_, _) => completed++;

        tracker.ManualStartBreak();
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(2));
        clock.StepWallClock(Step);
        tracker.Tick(TimeSpan.Zero);

        // A break the user took for two seconds must not earn the trusted rest reset.
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(0, completed);

        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(18));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Forward_clock_step_does_not_complete_a_break_early_without_activity_data()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, breakDuration: TimeSpan.FromSeconds(20));
        int completed = 0;
        tracker.BreakCompleted += (_, _) => completed++;

        tracker.ManualStartBreak();
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(2));
        clock.StepWallClock(Step);
        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Backward_clock_step_does_not_extend_a_break()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, breakDuration: TimeSpan.FromSeconds(20));
        int completed = 0;
        tracker.BreakCompleted += (_, _) => completed++;

        tracker.ManualStartBreak();
        clock.StepWallClock(-Step);
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, completed);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Forward_clock_step_does_not_inflate_accumulated_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromMinutes(10));

        tracker.Tick(TimeSpan.Zero);
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        clock.StepWallClock(Step);
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(1), tracker.AccumulatedWorkTime);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
    }

    [Fact]
    public void Backward_clock_step_does_not_shrink_accumulated_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromMinutes(10));

        tracker.Tick(TimeSpan.Zero);
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        clock.StepWallClock(-Step);
        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(2), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Forward_clock_step_does_not_end_a_timed_pause_early()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Pause(TimeSpan.FromMinutes(5));
        clock.StepWallClock(Step);
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);

        clock.AdvanceElapsedOnly(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Backward_clock_step_does_not_extend_a_timed_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Pause(TimeSpan.FromMinutes(5));
        clock.StepWallClock(-Step);
        clock.AdvanceElapsedOnly(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Forward_clock_step_does_not_end_focus_mode_early()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, focusModeDuration: TimeSpan.FromMinutes(10));
        int ended = 0;
        tracker.FocusModeEnded += (_, _) => ended++;

        tracker.StartFocusMode();
        clock.StepWallClock(Step);
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
        Assert.Equal(0, ended);

        clock.AdvanceElapsedOnly(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, ended);
    }

    [Fact]
    public void Backward_clock_step_does_not_extend_focus_mode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, focusModeDuration: TimeSpan.FromMinutes(10));
        int ended = 0;
        tracker.FocusModeEnded += (_, _) => ended++;

        tracker.StartFocusMode();
        clock.StepWallClock(-Step);
        clock.AdvanceElapsedOnly(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, ended);
    }

    [Fact]
    public void Forward_clock_step_does_not_end_a_snooze_early()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, snoozeDuration: TimeSpan.FromMinutes(3));
        ReachReminderVisible(tracker, clock);

        tracker.Snooze();
        clock.StepWallClock(Step);
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);

        clock.AdvanceElapsedOnly(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Forward_clock_step_does_not_complete_the_break_guide_early()
    {
        var clock = new FakeClock();
        var guide = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int completed = 0;
        guide.Completed += (_, _) => completed++;

        guide.Start();
        clock.StepWallClock(Step);
        guide.Tick();

        Assert.Equal(BreakGuidePhase.Running, guide.Phase);
        Assert.Equal(0, completed);

        clock.AdvanceElapsedOnly(TimeSpan.FromSeconds(20));
        guide.Tick();

        Assert.Equal(BreakGuidePhase.Completed, guide.Phase);
        Assert.Equal(1, completed);
    }

    private static void ReachReminderVisible(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 31; i++)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock clock,
        TimeSpan? workInterval = null,
        TimeSpan? breakDuration = null,
        TimeSpan? snoozeDuration = null,
        TimeSpan? focusModeDuration = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval ?? TimeSpan.FromSeconds(30),
            idleThreshold: TimeSpan.FromMinutes(3),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: breakDuration ?? TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(30),
            snoozeDuration: snoozeDuration ?? TimeSpan.FromMinutes(3),
            reminderDisplayDuration: TimeSpan.FromMinutes(30),
            retryCooldown: TimeSpan.FromMinutes(20),
            focusModeDuration: focusModeDuration ?? TimeSpan.FromMinutes(60));
        tracker.SetForceAllowPopup(true);
        return tracker;
    }
}
