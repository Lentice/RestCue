using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

/// <summary>
/// A held-back reminder attempt may only be promoted to a visible reminder while an
/// attempt is still pending. Lifting a presentation cap from any other phase — a break
/// above all — must leave the primary phase alone.
/// </summary>
public sealed class WorkCycleTrackerCapChangeGuardTests
{
    [Fact]
    public void Cap_lift_promotes_held_back_attempt_from_PendingReminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShown);
    }

    [Fact]
    public void Cap_lift_during_break_leaves_the_break_running()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        int reminderShown = 0;
        int breakCancelled = 0;
        int breakCompleted = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;
        tracker.BreakCancelled += (_, _) => breakCancelled++;
        tracker.BreakCompleted += (_, _) => breakCompleted++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        // The break still runs to completion and still counts as a trusted reset.
        for (int i = 0; i < 25 && tracker.CurrentPhase == WorkCyclePhase.BreakInProgress; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(1, breakCompleted);
        Assert.Equal(0, breakCancelled);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
    }

    [Fact]
    public void Cap_lift_while_paused_leaves_the_pause_intact()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        tracker.Pause();

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Cap_lift_while_disabled_leaves_reminders_disabled()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        tracker.Disable();

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Cap_lift_while_idle_does_not_wake_a_held_back_attempt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromMinutes(3));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Cap_lift_while_working_does_not_resurrect_a_stale_attempt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        tracker.Pause();
        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Cap_lift_while_snoozed_does_not_reopen_the_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        LiftCap(tracker);
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        tracker.Snooze();

        int reminderShown = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;

        RestrictCap(tracker);
        LiftCap(tracker);

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
    }

    [Fact]
    public void Manual_break_forgets_the_held_back_attempt()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        ReachHeldBackAttemptAtLevel3(tracker, clock);

        tracker.ManualStartBreak();

        int suppressed = 0;
        int reminderShown = 0;
        tracker.ReminderSuppressed += (_, _) => suppressed++;
        tracker.ReminderShown += (_, _) => reminderShown++;

        // Cancelling the break returns to a pending attempt, but the held-back one is
        // gone: nothing is re-announced and nothing pops when the cap later lifts.
        tracker.CancelBreak();
        LiftCap(tracker);

        Assert.Equal(0, suppressed);
        Assert.Equal(0, reminderShown);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    private static void RestrictCap(WorkCycleTracker tracker) =>
        tracker.SetIntensityCaps(PresentationIntensity.TrayOnly, PresentationIntensity.PopupAndSound);

    private static void LiftCap(WorkCycleTracker tracker) =>
        tracker.SetIntensityCaps(PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound);

    /// <summary>
    /// Drives the tracker to rest-debt Level 3 under a tray-only context cap, so that a
    /// reminder attempt has been held back to a tray cue and the phase is still
    /// PendingReminder. At Level 3 the debt recommendation permits an edge popup, which
    /// is what makes a later cap lift a promotion rather than a no-op.
    /// </summary>
    private static void ReachHeldBackAttemptAtLevel3(WorkCycleTracker tracker, FakeClock clock)
    {
        int suppressed = 0;
        void Count(object? sender, ReminderSuppressedEventArgs e) => suppressed++;
        tracker.ReminderSuppressed += Count;

        for (int i = 0; i < 21; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        tracker.ReminderSuppressed -= Count;

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level3, tracker.RestDebtLevel);
        Assert.Equal(1, suppressed);
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromSeconds(3),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromMinutes(5),
            retryCooldown: TimeSpan.FromMinutes(5),
            debtLevel2: TimeSpan.FromSeconds(15),
            debtLevel3: TimeSpan.FromSeconds(20),
            debtLevel4: TimeSpan.FromHours(4));
        RestrictCap(tracker);
        return tracker;
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset _utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
