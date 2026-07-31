using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class WorkCycleTrackerNewEventSeamTests
{
    [Fact]
    public void IdleStarted_fires_when_entering_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int idleStartedCount = 0;
        tracker.IdleStarted += (_, _) => idleStartedCount++;

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromMinutes(5));

        Assert.Equal(1, idleStartedCount);
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void IdleEnded_fires_when_exiting_idle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int idleEndedCount = 0;
        tracker.IdleEnded += (_, _) => idleEndedCount++;

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromMinutes(5));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, idleEndedCount);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakStarted_fires_from_StartBreak()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int breakStartedCount = 0;
        tracker.BreakStarted += (_, _) => breakStartedCount++;

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        Assert.Equal(1, breakStartedCount);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakStarted_fires_from_ManualStartBreak()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int breakStartedCount = 0;
        tracker.BreakStarted += (_, _) => breakStartedCount++;

        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.ManualStartBreak();

        Assert.Equal(1, breakStartedCount);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakCancelled_fires_when_resume_interrupts_break()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int breakCancelledCount = 0;
        tracker.BreakCancelled += (_, _) => breakCancelledCount++;

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        tracker.HandleSleep();
        tracker.HandleResume();

        Assert.Equal(1, breakCancelledCount);
        Assert.NotEqual(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakCancelled_fires_when_unlock_interrupts_break()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int breakCancelledCount = 0;
        tracker.BreakCancelled += (_, _) => breakCancelledCount++;

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        tracker.HandleLock();
        tracker.HandleUnlock();

        Assert.Equal(1, breakCancelledCount);
        Assert.NotEqual(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakCompleted_does_not_fire_BreakCancelled()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, breakDuration: TimeSpan.FromSeconds(5));
        tracker.SetForceAllowPopup(true);
        int breakCancelledCount = 0;
        int breakCompletedCount = 0;
        tracker.BreakCancelled += (_, _) => breakCancelledCount++;
        tracker.BreakCompleted += (_, _) => breakCompletedCount++;

        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(0, breakCancelledCount);
        Assert.Equal(1, breakCompletedCount);
    }

    [Fact]
    public void CooldownStarted_fires_on_Ignore()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int cooldownStartedCount = 0;
        int reminderDismissedCount = 0;
        tracker.CooldownStarted += (_, _) => cooldownStartedCount++;
        tracker.ReminderDismissed += (_, _) => reminderDismissedCount++;

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();

        Assert.Equal(1, cooldownStartedCount);
        Assert.Equal(1, reminderDismissedCount);
    }

    [Fact]
    public void CooldownStarted_fires_on_AutoDismissed()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(30),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(3),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: TimeSpan.FromSeconds(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(3),
            retryCooldown: TimeSpan.FromMinutes(20));
        tracker.SetForceAllowPopup(true);
        int cooldownStartedCount = 0;
        tracker.CooldownStarted += (_, _) => cooldownStartedCount++;

        ReachReminderVisible(tracker, clock);
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, cooldownStartedCount);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void CooldownEnded_fires_when_reminder_shown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int cooldownEndedCount = 0;
        tracker.CooldownEnded += (_, _) => cooldownEndedCount++;

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromMinutes(21));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(1, cooldownEndedCount);
    }

    [Fact]
    public void CooldownEnded_state_is_null_during_handler_on_all_paths()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        TimeSpan? capturedDuringCooldownEnded = null;
        int cooldownEndedCount = 0;
        tracker.CooldownEnded += (_, _) =>
        {
            cooldownEndedCount++;
            capturedDuringCooldownEnded = tracker.CooldownUntil;
        };

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromMinutes(21));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(1, cooldownEndedCount);
        Assert.Null(capturedDuringCooldownEnded);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromMinutes(5));
        Assert.Equal(2, cooldownEndedCount);
        Assert.Null(capturedDuringCooldownEnded);
    }

    [Fact]
    public void CooldownEnded_fires_when_idle_resets_cooldown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetForceAllowPopup(true);
        int cooldownEndedCount = 0;
        tracker.CooldownEnded += (_, _) => cooldownEndedCount++;

        ReachReminderVisible(tracker, clock);
        tracker.Ignore();
        Assert.NotNull(tracker.CooldownUntil);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.FromMinutes(5));

        Assert.Equal(1, cooldownEndedCount);
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderLightTouch_fires_when_caps_upgrade_to_LightTouch()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(1),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(3),
            maximumReminderWait: TimeSpan.FromSeconds(0.1),
            breakDuration: TimeSpan.FromSeconds(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromSeconds(0.1),
            debtLevel2: TimeSpan.FromSeconds(2),
            debtLevel3: TimeSpan.FromSeconds(3),
            debtLevel4: TimeSpan.FromSeconds(4));
        tracker.SetForceAllowPopup(true);
        tracker.SetIntensityCaps(PresentationIntensity.LightTouch, PresentationIntensity.LightTouch);

        for (int cycle = 0; cycle < 10; cycle++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.5));
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(0.5));
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(0.5));
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(0.5));
            tracker.Tick(TimeSpan.Zero);

            if (tracker.CurrentPhase == WorkCyclePhase.PendingReminder)
            {
                clock.Advance(TimeSpan.FromSeconds(0.5));
                tracker.Tick(TimeSpan.Zero);
            }
            if (tracker.CurrentPhase == WorkCyclePhase.ReminderVisible)
            {
                tracker.Ignore();
                clock.Advance(TimeSpan.FromSeconds(0.5));
                tracker.Tick(TimeSpan.Zero);
            }
        }

        tracker.SetForceAllowPopup(false);

        int lightTouchCount = 0;
        tracker.ReminderLightTouch += (_, _) => lightTouchCount++;

        for (int i = 0; i < 3; i++) { clock.Advance(TimeSpan.FromSeconds(0.5)); tracker.Tick(TimeSpan.Zero); }
        clock.Advance(TimeSpan.FromSeconds(0.5));
        tracker.Tick(TimeSpan.Zero);

        Assert.True(lightTouchCount >= 1, $"Expected ReminderLightTouch to fire at least once, fired {lightTouchCount}");
    }

    [Fact]
    public void ReminderLightTouch_does_not_refire_when_caps_unchanged()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        tracker.SetIntensityCaps(PresentationIntensity.TrayOnly, PresentationIntensity.TrayOnly);
        int lightTouchCount = 0;
        tracker.ReminderLightTouch += (_, _) => lightTouchCount++;

        for (int i = 0; i < 31; i++) { clock.Advance(TimeSpan.FromSeconds(1)); tracker.Tick(TimeSpan.Zero); }
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.FromSeconds(5));

        tracker.SetIntensityCaps(PresentationIntensity.LightTouch, PresentationIntensity.LightTouch);
        tracker.SetIntensityCaps(PresentationIntensity.LightTouch, PresentationIntensity.LightTouch);

        Assert.Equal(0, lightTouchCount);
    }

    private static WorkCycleTracker CreateTracker(IClock clock, TimeSpan? breakDuration = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromSeconds(30),
            idleThreshold: TimeSpan.FromMinutes(2),
            naturalPauseThreshold: TimeSpan.FromSeconds(3),
            maximumReminderWait: TimeSpan.FromMinutes(3),
            breakDuration: breakDuration ?? TimeSpan.FromSeconds(10),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromMinutes(20));
        return tracker;
    }

    private static void ReachReminderVisible(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 31; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        if (tracker.CurrentPhase == WorkCyclePhase.PendingReminder)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            tracker.Tick(TimeSpan.FromSeconds(5));
        }
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
