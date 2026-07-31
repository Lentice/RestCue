using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

/// <summary>
/// Every path out of Focus Mode must record exactly one end, so that daily statistics
/// never accumulate a start without a matching end.
/// </summary>
public sealed class WorkCycleTrackerFocusModeExitTests
{
    private static readonly TimeSpan FocusModeDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public void Timer_expiry_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        clock.Advance(FocusModeDuration);
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Timer_expiry_while_activity_is_unavailable_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        clock.Advance(FocusModeDuration);
        tracker.TickActivityUnavailable();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Explicit_end_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.EndFocusMode();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Explicit_end_that_lands_in_PendingReminder_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        ReachPendingReminder(tracker, clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.EndFocusMode();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_entry_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void Session_lock_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.HandleLock();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        tracker.HandleUnlock();
        Assert.Equal(1, ended());
    }

    [Fact]
    public void System_sleep_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.HandleSleep();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        tracker.HandleResume();
        Assert.Equal(1, ended());
    }

    [Fact]
    public void Manual_break_start_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.ManualStartBreak();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void Disabling_reminders_ends_focus_mode_once()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        var ended = CountEnds(tracker);

        tracker.StartFocusMode();
        tracker.Disable();

        Assert.Equal(1, ended());
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    [Fact]
    public void Starts_and_ends_stay_balanced_across_mixed_exit_paths()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        int started = 0;
        int ended = 0;
        tracker.FocusModeStarted += (_, _) => started++;
        tracker.FocusModeEnded += (_, _) => ended++;

        tracker.StartFocusMode();
        tracker.EndFocusMode();

        tracker.StartFocusMode();
        tracker.HandleLock();
        tracker.HandleUnlock();

        tracker.StartFocusMode();
        tracker.ManualStartBreak();
        tracker.CancelBreak();

        tracker.StartFocusMode();
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        tracker.Tick(TimeSpan.Zero);

        tracker.StartFocusMode();
        clock.Advance(FocusModeDuration);
        tracker.Tick(TimeSpan.Zero);

        tracker.StartFocusMode();
        tracker.HandleSleep();
        tracker.HandleResume();

        tracker.StartFocusMode();
        tracker.Disable();
        tracker.Enable();

        Assert.Equal(7, started);
        Assert.Equal(started, ended);
    }

    private static Func<int> CountEnds(WorkCycleTracker tracker)
    {
        int ended = 0;
        tracker.FocusModeEnded += (_, _) => ended++;
        return () => ended;
    }

    private static void ReachPendingReminder(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 21 && tracker.CurrentPhase == WorkCyclePhase.Working; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromMinutes(20),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromMinutes(20),
            focusModeDuration: FocusModeDuration);
        tracker.SetForceAllowPopup(true);
        return tracker;
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
