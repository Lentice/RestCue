using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

/// <summary>
/// The settings validator is the authority on the settings contract, and it accepts a
/// maximum reminder wait of zero — meaning "Timing is eligible as soon as a reminder is
/// pending". The tracker's construction guard yields to it. Timing eligibility must not
/// raise presentation intensity.
/// </summary>
public sealed class WorkCycleTrackerZeroMaxWaitTests
{
    [Fact]
    public void Zero_maximum_reminder_wait_is_accepted()
    {
        var clock = new FakeClock();

        var tracker = CreateTracker(clock, TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Negative_maximum_reminder_wait_is_still_rejected()
    {
        var clock = new FakeClock();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTracker(clock, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Zero_maximum_reminder_wait_makes_timing_eligible_as_soon_as_a_reminder_is_pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, TimeSpan.Zero);
        tracker.SetForceAllowPopup(true);

        for (int i = 0; i < 11; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        // No natural pause and no waiting: the first evaluation of the pending state
        // is already Timing-eligible.
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void Zero_maximum_reminder_wait_at_debt_Level1_still_only_yields_a_tray_cue()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, TimeSpan.Zero);

        int reminderShown = 0;
        int trayCues = 0;
        tracker.ReminderShown += (_, _) => reminderShown++;
        tracker.ReminderSuppressed += (_, e) => { if (e.ShowTrayCue) trayCues++; };

        for (int i = 0; i < 13; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);
        Assert.Equal(1, trayCues);
    }

    [Fact]
    public void Updated_snooze_duration_applies_to_the_next_snooze()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, TimeSpan.FromSeconds(3));
        tracker.SetForceAllowPopup(true);

        tracker.UpdateSnoozeDuration(TimeSpan.FromMinutes(1));

        for (int i = 0; i < 20 && tracker.CurrentPhase != WorkCyclePhase.ReminderVisible; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        tracker.Snooze();

        // The old five-minute default would still be snoozing here.
        clock.Advance(TimeSpan.FromSeconds(59));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void A_snooze_already_running_keeps_the_deadline_it_was_given()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, TimeSpan.FromSeconds(3));
        tracker.SetForceAllowPopup(true);

        for (int i = 0; i < 20 && tracker.CurrentPhase != WorkCyclePhase.ReminderVisible; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        tracker.Snooze();
        tracker.UpdateSnoozeDuration(TimeSpan.FromSeconds(1));

        // Shortening the setting must not cut short the snooze the user already asked for.
        clock.Advance(TimeSpan.FromSeconds(2));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void A_non_positive_snooze_duration_is_rejected()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, TimeSpan.FromMinutes(3));

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.UpdateSnoozeDuration(TimeSpan.Zero));
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock, TimeSpan maximumReminderWait)
    {
        return new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: maximumReminderWait,
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromMinutes(5),
            debtLevel2: TimeSpan.FromSeconds(35),
            debtLevel3: TimeSpan.FromSeconds(45),
            debtLevel4: TimeSpan.FromSeconds(60));
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
