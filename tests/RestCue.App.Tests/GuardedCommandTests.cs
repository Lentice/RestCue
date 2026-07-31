using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// A rejected command must cost the user nothing, and no reminder action may terminate the
/// application.
/// </summary>
public sealed class GuardedCommandTests
{
    [Fact]
    public void Rejected_pause_leaves_a_running_break_intact()
    {
        // Idle refuses pause outright — there is no preparatory step that makes it legal.
        var clock = new FakeClock();
        var tracker = DriveToPhase(clock, WorkCyclePhase.Idle);

        int closed = 0;

        bool applied = App.ExecutePause(tracker, () => closed++);

        Assert.False(applied);
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Rejected_focus_mode_leaves_the_reminder_surface_open()
    {
        var clock = new FakeClock();
        var tracker = DriveToPhase(clock, WorkCyclePhase.Disabled);

        int closed = 0;

        bool applied = App.ExecuteStartFocusMode(tracker, () => closed++);

        Assert.False(applied);
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Rejected_resume_changes_nothing()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        Assert.False(App.ExecuteResume(tracker));
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Pausing_during_a_break_cancels_the_break_and_reaches_Paused()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.ManualStartBreak();

        int cancelled = 0;
        int closed = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        bool applied = App.ExecutePause(tracker, () => closed++);

        Assert.True(applied);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(1, cancelled);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Timed_pause_during_a_break_cancels_the_break_and_reaches_Paused()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.ManualStartBreak();

        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        bool applied = App.ExecutePauseFor(tracker, TimeSpan.FromMinutes(15), () => { });

        Assert.True(applied);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Focus_mode_during_a_break_cancels_the_break_and_reaches_FocusMode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.ManualStartBreak();

        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        bool applied = App.ExecuteStartFocusMode(tracker, () => { });

        Assert.True(applied);
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Pausing_outside_a_break_records_no_cancellation()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        Assert.True(App.ExecutePause(tracker, () => { }));
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void Accepted_pause_closes_the_reminder_and_reaches_Paused()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int closed = 0;

        bool applied = App.ExecutePause(tracker, () => closed++);

        Assert.True(applied);
        Assert.Equal(1, closed);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void Disabling_during_a_break_cancels_the_break_as_a_recorded_step()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.ManualStartBreak();

        int cancelled = 0;
        int closed = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        bool applied = App.ExecuteDisable(tracker, () => closed++);

        Assert.True(applied);
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(1, cancelled);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Disabling_outside_a_break_records_no_cancellation()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        Assert.True(App.ExecuteDisable(tracker, () => { }));
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void Disabling_twice_is_refused_rather_than_throwing()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        Assert.True(App.ExecuteDisable(tracker, () => { }));

        Assert.False(App.ExecuteDisable(tracker, () => { }));
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
    }

    // Every reminder action, invoked from a phase where the engine would refuse it.
    public static TheoryData<string> ReminderActions() =>
        new() { "StartBreak", "Snooze", "Ignore" };

    [Theory]
    [MemberData(nameof(ReminderActions))]
    public void Reminder_action_from_a_rejecting_phase_neither_throws_nor_changes_state(string action)
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        // Working: the reminder has already been dismissed by the time the click lands.
        WorkCyclePhase before = tracker.CurrentPhase;
        TimeSpan needBefore = tracker.AccumulatedWorkTime;

        bool applied = Invoke(action, tracker);

        Assert.False(applied);
        Assert.Equal(before, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Start_break_from_a_visible_reminder_starts_the_break()
    {
        var clock = new FakeClock();
        var tracker = ReachReminderVisible(clock);

        Assert.True(App.ExecuteStartBreak(tracker));
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void Snooze_from_a_visible_reminder_records_the_result()
    {
        var clock = new FakeClock();
        var tracker = ReachReminderVisible(clock);
        ReminderResult? result = null;
        tracker.ReminderDismissed += (_, e) => result = e.Result;

        Assert.True(App.ExecuteSnooze(tracker));
        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
        Assert.Equal(ReminderResult.Snoozed, result);
    }

    [Fact]
    public void Ignore_from_a_visible_reminder_records_the_result()
    {
        var clock = new FakeClock();
        var tracker = ReachReminderVisible(clock);
        ReminderResult? result = null;
        tracker.ReminderDismissed += (_, e) => result = e.Result;

        Assert.True(App.ExecuteIgnore(tracker));
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(ReminderResult.Ignored, result);
    }

    [Fact]
    public void Double_activation_of_start_break_starts_one_break()
    {
        var clock = new FakeClock();
        var tracker = ReachReminderVisible(clock);
        int started = 0;
        tracker.BreakStarted += (_, _) => started++;

        Assert.True(App.ExecuteStartBreak(tracker));
        Assert.False(App.ExecuteStartBreak(tracker));

        Assert.Equal(1, started);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    /// <summary>
    /// Every guarded command must agree with the availability policy in every phase, or a
    /// surface could offer something the guard then refuses.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Guards_agree_with_the_availability_policy(WorkCyclePhase phase)
    {
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        Assert.Equal(expected.CanPause, Run(phase, t => App.ExecutePause(t, () => { })));
        Assert.Equal(expected.CanPause, Run(phase, t => App.ExecutePauseFor(t, TimeSpan.FromMinutes(15), () => { })));
        Assert.Equal(expected.CanResume, Run(phase, App.ExecuteResume));
        Assert.Equal(expected.CanStartFocusMode, Run(phase, t => App.ExecuteStartFocusMode(t, () => { })));
        Assert.Equal(expected.CanEndFocusMode, Run(phase, App.ExecuteEndFocusMode));
        Assert.Equal(expected.CanDisable, Run(phase, t => App.ExecuteDisable(t, () => { })));
        Assert.Equal(expected.CanEnable, Run(phase, App.ExecuteEnable));
        Assert.Equal(expected.CanBreakNow, Run(phase, t => App.ExecuteManualStartBreak(t, () => { })));
    }

    private static bool Run(WorkCyclePhase phase, Func<WorkCycleTracker, bool> command)
    {
        var clock = new FakeClock();
        return command(DriveToPhase(clock, phase));
    }

    private static bool Invoke(string action, WorkCycleTracker tracker) => action switch
    {
        "StartBreak" => App.ExecuteStartBreak(tracker),
        "Snooze" => App.ExecuteSnooze(tracker),
        "Ignore" => App.ExecuteIgnore(tracker),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };

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
                DriveToReminderVisible(tracker, clock);
                break;
            case WorkCyclePhase.BreakInProgress:
                tracker.ManualStartBreak();
                break;
            case WorkCyclePhase.Snoozed:
                DriveToReminderVisible(tracker, clock);
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

    private static WorkCycleTracker ReachReminderVisible(FakeClock clock)
    {
        var tracker = CreateTracker(clock);
        DriveToReminderVisible(tracker, clock);
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

    private static void DriveToReminderVisible(WorkCycleTracker tracker, FakeClock clock)
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
