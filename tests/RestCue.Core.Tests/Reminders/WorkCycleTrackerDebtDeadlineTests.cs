using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

/// <summary>
/// The retry cooldown must not delay re-evaluation past the next rest-debt threshold
/// (ADR-0003). The threshold deadline is therefore armed when the cooldown starts, and
/// the earlier-of-two-deadlines gate fires on the first threshold crossing.
/// </summary>
public sealed class WorkCycleTrackerDebtDeadlineTests
{
    [Fact]
    public void Ignore_at_default_settings_re_evaluates_at_the_next_threshold_not_at_cooldown_expiry()
    {
        var clock = new FakeClock();
        var tracker = CreateDefaultSettingsTracker(clock);

        ReachReminderVisibleAtDefaultSettings(tracker, clock);
        tracker.Ignore();

        var cooldownUntil = tracker.CooldownUntil;
        Assert.NotNull(cooldownUntil);

        // 21 minutes of work accumulated; Level 2 is 35 minutes away in wall-clock terms
        // at 14 minutes' time, six minutes before the 20-minute cooldown would expire.
        for (int i = 0; i < 13; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        clock.Advance(TimeSpan.FromMinutes(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
        Assert.True(clock.UtcNow < cooldownUntil!.Value,
            "The threshold crossing, not the cooldown expiry, must have driven the re-evaluation.");

        // Ignore() ends the current attempt and drops its accumulation bookkeeping, so
        // one tick of work is never credited and the wall-clock deadline lands one tick
        // short of the 35-minute threshold. Arriving early is harmless; the defect was
        // arriving a whole level late.
        Assert.InRange(
            tracker.AccumulatedWorkTime,
            TimeSpan.FromMinutes(34),
            TimeSpan.FromMinutes(35));
    }

    [Fact]
    public void Auto_dismiss_re_evaluates_at_the_next_threshold_like_an_explicit_ignore()
    {
        var clock = new FakeClock();
        var tracker = CreateDefaultSettingsTracker(clock, reminderDisplay: TimeSpan.FromMinutes(1));

        ReachReminderVisibleAtDefaultSettings(tracker, clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        var cooldownUntil = tracker.CooldownUntil;
        Assert.NotNull(cooldownUntil);

        for (int i = 0; i < 20 && tracker.CurrentPhase == WorkCyclePhase.Working; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
        Assert.True(clock.UtcNow < cooldownUntil!.Value,
            "The threshold crossing, not the cooldown expiry, must have driven the re-evaluation.");
    }

    [Fact]
    public void Ignore_at_the_highest_level_lets_the_cooldown_govern_alone()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock,
            debtLevel2: TimeSpan.FromSeconds(15),
            debtLevel3: TimeSpan.FromSeconds(20),
            debtLevel4: TimeSpan.FromSeconds(25),
            retryCooldown: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 26; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(RestDebtLevel.Level4, tracker.RestDebtLevel);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);

        tracker.Ignore();
        var cooldownUntil = tracker.CooldownUntil!.Value;

        // There is no further threshold, so nothing may pre-empt the cooldown.
        while (clock.UtcNow < cooldownUntil - TimeSpan.FromSeconds(1))
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    [Fact]
    public void Completed_break_discards_the_armed_threshold_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        IgnoreAtLevel1(tracker, clock);

        tracker.ManualStartBreak();

        for (int i = 0; i < 25 && tracker.CurrentPhase == WorkCyclePhase.BreakInProgress; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        // The discarded deadline is long past; the fresh cycle must run its full interval.
        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        }
    }

    [Fact]
    public void Idle_entry_discards_the_armed_threshold_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        IgnoreAtLevel1(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromMinutes(5));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        }
    }

    [Fact]
    public void Pause_and_resume_preserve_the_armed_threshold_deadline()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        IgnoreAtLevel1(tracker, clock);

        var cooldownUntil = tracker.CooldownUntil;
        tracker.Pause();

        // Pause is a freeze: the deadline passes but nothing re-evaluates.
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        }

        Assert.Equal(cooldownUntil, tracker.CooldownUntil);

        tracker.Resume();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Null(tracker.CooldownUntil);
    }

    /// <summary>
    /// Drives the tracker to a Level 1 ignore, arming a threshold deadline four seconds
    /// out against a five-minute cooldown.
    /// </summary>
    private static void IgnoreAtLevel1(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 11; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);

        tracker.Ignore();
    }

    private static void ReachReminderVisibleAtDefaultSettings(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 22 && tracker.CurrentPhase != WorkCyclePhase.ReminderVisible; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
    }

    private static WorkCycleTracker CreateDefaultSettingsTracker(
        FakeClock clock, TimeSpan? reminderDisplay = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromMinutes(20),
            idleThreshold: TimeSpan.FromMinutes(5),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromMinutes(1),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: reminderDisplay ?? TimeSpan.FromMinutes(10),
            retryCooldown: TimeSpan.FromMinutes(20),
            debtLevel2: TimeSpan.FromMinutes(35),
            debtLevel3: TimeSpan.FromMinutes(45),
            debtLevel4: TimeSpan.FromMinutes(60));
        tracker.SetForceAllowPopup(true);
        return tracker;
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock clock,
        TimeSpan? debtLevel2 = null,
        TimeSpan? debtLevel3 = null,
        TimeSpan? debtLevel4 = null,
        TimeSpan? retryCooldown = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromMinutes(5),
            naturalPauseThreshold: TimeSpan.FromSeconds(5),
            maximumReminderWait: TimeSpan.FromHours(1),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromMinutes(10),
            retryCooldown: retryCooldown ?? TimeSpan.FromMinutes(5),
            debtLevel2: debtLevel2 ?? TimeSpan.FromSeconds(20),
            debtLevel3: debtLevel3 ?? TimeSpan.FromSeconds(30),
            debtLevel4: debtLevel4 ?? TimeSpan.FromSeconds(40));
        tracker.SetForceAllowPopup(true);
        return tracker;
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset _utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
