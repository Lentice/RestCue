using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class WorkCycleTrackerForegroundContextTests
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
    public void Fullscreen_suppresses_reminder_shown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) => suppressedCount++;

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
    }

    [Fact]
    public void Fullscreen_suppressed_reminder_shows_when_leaving_fullscreen()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) => suppressedCount++;

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
        Assert.Equal(1, suppressedCount);
    }

    [Fact]
    public void Leaving_fullscreen_does_not_show_reminder_if_no_longer_pending()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(25));
        tracker.Tick(TimeSpan.FromSeconds(25));

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) => suppressedCount++;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);
        Assert.Equal(0, suppressedCount);
    }

    [Fact]
    public void No_suppression_when_not_fullscreen()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Rule_suppress_reminder_suppresses_shown()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) => suppressedCount++;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
    }

    [Fact]
    public void Rule_suppress_releases_when_context_removed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Custom_work_interval_used_instead_of_default()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);

        tracker.UpdateForegroundContext(
            fullscreen: false,
            suppressReminder: false,
            showTrayCue: false,
            effectiveWorkIntervalOverride: TimeSpan.FromSeconds(10));

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Custom_work_interval_does_not_reset_accumulated_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        var before = tracker.AccumulatedWorkTime;

        tracker.UpdateForegroundContext(
            fullscreen: false,
            suppressReminder: false,
            showTrayCue: false,
            effectiveWorkIntervalOverride: TimeSpan.FromSeconds(10));

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Removing_custom_interval_restores_default_work_interval()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.UpdateForegroundContext(
            fullscreen: false,
            suppressReminder: false,
            showTrayCue: false,
            effectiveWorkIntervalOverride: TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(9));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(
            fullscreen: false,
            suppressReminder: false,
            showTrayCue: false,
            effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Suppressed_event_does_not_change_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void Fullscreen_does_not_suppress_accumulation()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(5), tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Fullscreen_natural_pause_suppressed_then_leaving_fullscreen_shows_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(3));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, _) => suppressedCount++;

        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.FromSeconds(5));

        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Fullscreen_max_wait_suppressed_then_leaving_fullscreen_shows_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            maxWait: TimeSpan.FromMinutes(3));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        int reminderShownCount = 0;
        int suppressedCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, _) => suppressedCount++;

        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void TrayOnly_suppression_fires_with_showTrayCue_true()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        bool? capturedShowTrayCue = null;
        tracker.ReminderSuppressed += (_, e) => capturedShowTrayCue = e.ShowTrayCue;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.True(capturedShowTrayCue);
    }

    [Fact]
    public void Silent_suppression_fires_with_showTrayCue_false()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        bool? capturedShowTrayCue = null;
        tracker.ReminderSuppressed += (_, e) => capturedShowTrayCue = e.ShowTrayCue;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.False(capturedShowTrayCue);
    }

    [Fact]
    public void Fullscreen_suppression_fires_with_showTrayCue_true()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        bool? capturedShowTrayCue = null;
        tracker.ReminderSuppressed += (_, e) => capturedShowTrayCue = e.ShowTrayCue;

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: true, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.True(capturedShowTrayCue);
    }

    [Fact]
    public void Fullscreen_suppression_fires_without_tray_cue()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);

        bool? capturedShowTrayCue = null;
        tracker.ReminderSuppressed += (_, e) => capturedShowTrayCue = e.ShowTrayCue;

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.False(capturedShowTrayCue);
    }

    [Fact]
    public void TrayOnly_context_change_shows_deferred_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Silent_context_change_shows_deferred_reminder()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        int reminderShownCount = 0;
        tracker.ReminderShown += (_, _) => reminderShownCount++;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
        Assert.Equal(1, reminderShownCount);
    }

    [Fact]
    public void Accumulated_time_preserved_during_foreground_context_updates()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);

        var before = tracker.AccumulatedWorkTime;

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);
        Assert.Equal(before, tracker.AccumulatedWorkTime);

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);
        Assert.Equal(before, tracker.AccumulatedWorkTime);

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);
        Assert.Equal(before, tracker.AccumulatedWorkTime);

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: TimeSpan.FromMinutes(10));
        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Fullscreen_to_TrayOnly_maintains_suppressed_state()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        int reminderShownCount = 0;
        int suppressedCount = 0;
        bool? capturedShowTrayCue = null;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) =>
        {
            suppressedCount++;
            capturedShowTrayCue = e.ShowTrayCue;
        };

        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
        Assert.True(capturedShowTrayCue);
    }

    [Fact]
    public void TrayOnly_to_fullscreen_clears_tray_cue()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock: clock, naturalPause: TimeSpan.FromSeconds(5));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        int reminderShownCount = 0;
        int suppressedCount = 0;
        bool? capturedShowTrayCue = null;
        tracker.ReminderShown += (_, _) => reminderShownCount++;
        tracker.ReminderSuppressed += (_, e) =>
        {
            suppressedCount++;
            capturedShowTrayCue = e.ShowTrayCue;
        };

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(0, reminderShownCount);
        Assert.Equal(1, suppressedCount);
        Assert.False(capturedShowTrayCue);
    }

    [Fact]
    public void Fullscreen_to_TrayOnly_keeps_accumulated_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            workInterval: TimeSpan.FromMinutes(20),
            naturalPause: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(5));
        tracker.Tick(TimeSpan.Zero);
        var before = tracker.AccumulatedWorkTime;

        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);
        tracker.UpdateForegroundContext(fullscreen: false, suppressReminder: true, showTrayCue: true, effectiveWorkIntervalOverride: null);

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void Passive_pause_detected_during_fullscreen()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(
            clock: clock,
            passiveBreak: TimeSpan.FromSeconds(20));

        ReachPendingReminder(tracker, clock);
        tracker.UpdateForegroundContext(fullscreen: true, suppressReminder: false, showTrayCue: false, effectiveWorkIntervalOverride: null);

        int pauseFired = 0;
        tracker.PassivePauseDetected += (_, _) => pauseFired++;

        clock.Advance(TimeSpan.FromSeconds(21));
        tracker.Tick(TimeSpan.FromSeconds(21));

        Assert.Equal(1, pauseFired);
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.NotEqual(TimeSpan.Zero, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void EffectiveWorkInterval_override_does_not_change_debt_level()
    {
        var clock = new FakeClock();
        var tracker = new WorkCycleTracker(
            clock,
            workInterval: TimeSpan.FromMinutes(20),
            idleThreshold: TimeSpan.FromHours(2),
            naturalPauseThreshold: TimeSpan.FromHours(1),
            maximumReminderWait: TimeSpan.FromHours(3),
            breakDuration: TimeSpan.FromSeconds(20),
            passiveBreakThreshold: TimeSpan.FromSeconds(20),
            snoozeDuration: TimeSpan.FromMinutes(5),
            reminderDisplayDuration: TimeSpan.FromSeconds(30),
            retryCooldown: TimeSpan.FromHours(4));

        tracker.Tick(TimeSpan.Zero);

        tracker.UpdateForegroundContext(
            fullscreen: false,
            suppressReminder: false,
            showTrayCue: false,
            effectiveWorkIntervalOverride: TimeSpan.FromMinutes(60));

        int debtChanged = 0;
        RestDebtLevel changedLevel = RestDebtLevel.Level0;
        tracker.RestDebtLevelChanged += (_, args) =>
        {
            debtChanged++;
            changedLevel = args.Current;
        };

        for (int i = 0; i < 21 * 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
        Assert.Equal(1, debtChanged);
        Assert.Equal(RestDebtLevel.Level1, changedLevel);

        for (int i = 0; i < 16 * 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(RestDebtLevel.Level2, tracker.RestDebtLevel);
        Assert.Equal(2, debtChanged);
        Assert.Equal(RestDebtLevel.Level2, changedLevel);
    }

    private static readonly TimeSpan DefaultRetryCooldown = TimeSpan.FromSeconds(1);

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
