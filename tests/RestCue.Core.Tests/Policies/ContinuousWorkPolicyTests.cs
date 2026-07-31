using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class ContinuousWorkPolicyTests
{
    [Theory]
    [InlineData(WorkCyclePhase.Working, true)]
    [InlineData(WorkCyclePhase.PendingReminder, true)]
    [InlineData(WorkCyclePhase.ReminderVisible, true)]
    [InlineData(WorkCyclePhase.Snoozed, true)]
    [InlineData(WorkCyclePhase.FocusMode, true)]
    [InlineData(WorkCyclePhase.BreakInProgress, false)]
    [InlineData(WorkCyclePhase.Idle, false)]
    [InlineData(WorkCyclePhase.Paused, false)]
    [InlineData(WorkCyclePhase.Disabled, false)]
    public void Predicate_table(WorkCyclePhase phase, bool isContinuousWork)
    {
        Assert.Equal(isContinuousWork, ContinuousWorkPolicy.IsContinuousWork(phase));
    }

    [Fact]
    public void Every_phase_is_classified()
    {
        // A phase added later must be considered deliberately, not silently default to
        // "not work" and quietly fragment the statistics.
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            _ = ContinuousWorkPolicy.IsContinuousWork(phase);
        }

        Assert.Equal(9, Enum.GetValues<WorkCyclePhase>().Length);
    }

    /// <summary>
    /// A stretch of work through which the user ignored reminders is one stretch. Driven
    /// through the real engine, counting the work-session transitions the application would
    /// have emitted.
    /// </summary>
    [Fact]
    public void Reminder_lifecycle_does_not_interrupt_a_work_stretch()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var sessions = new WorkSessionRecorder(tracker.CurrentPhase);

        // Reminder shown, snoozed, shown again, ignored, and back to working.
        Advance(tracker, clock, sessions, 11);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        sessions.Observe(tracker.CurrentPhase);
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        tracker.Snooze();
        sessions.Observe(tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);
        sessions.Observe(tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        sessions.Observe(tracker.CurrentPhase);
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        tracker.Ignore();
        sessions.Observe(tracker.CurrentPhase);

        Advance(tracker, clock, sessions, 5);

        Assert.Equal(1, sessions.Started);
        Assert.Equal(0, sessions.Ended);
    }

    [Theory]
    [InlineData(WorkCyclePhase.BreakInProgress)]
    [InlineData(WorkCyclePhase.Idle)]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.Disabled)]
    public void Each_genuine_interruption_ends_the_stretch(WorkCyclePhase interrupting)
    {
        var sessions = new WorkSessionRecorder(WorkCyclePhase.Working);

        sessions.Observe(interrupting);

        Assert.Equal(1, sessions.Started);
        Assert.Equal(1, sessions.Ended);
    }

    [Fact]
    public void Focus_mode_keeps_the_stretch_running()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var sessions = new WorkSessionRecorder(tracker.CurrentPhase);

        tracker.StartFocusMode();
        sessions.Observe(tracker.CurrentPhase);
        tracker.EndFocusMode();
        sessions.Observe(tracker.CurrentPhase);

        Assert.Equal(1, sessions.Started);
        Assert.Equal(0, sessions.Ended);
    }

    private static void Advance(
        WorkCycleTracker tracker, FakeClock clock, WorkSessionRecorder sessions, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            sessions.Observe(tracker.CurrentPhase);
        }
    }

    /// <summary>
    /// Mirrors the application's work-session emitter: transitions of the predicate, not
    /// equality against a phase.
    /// </summary>
    private sealed class WorkSessionRecorder
    {
        private bool wasWorking;

        public WorkSessionRecorder(WorkCyclePhase initialPhase)
        {
            Observe(initialPhase);
        }

        public int Started { get; private set; }

        public int Ended { get; private set; }

        public void Observe(WorkCyclePhase phase)
        {
            bool isWorking = ContinuousWorkPolicy.IsContinuousWork(phase);
            if (isWorking && !wasWorking)
                Started++;
            else if (!isWorking && wasWorking)
                Ended++;
            wasWorking = isWorking;
        }
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromMinutes(30),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromMinutes(5),
            retryCooldown: TimeSpan.FromMinutes(30),
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
