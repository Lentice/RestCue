using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class StateTransitionScenarioTests
{
    private static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultNaturalPause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultPassiveBreak = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultSnoozeDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultReminderDisplay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultRetryCooldown = TimeSpan.FromMinutes(20);

    [Fact]
    public void Starts_in_Working_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Working_to_PendingReminder_after_work_interval()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 31; i++)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level1, tracker.RestDebtLevel);
    }

    [Fact]
    public void Working_to_Idle_when_idle_detected()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, idleThreshold: TimeSpan.FromSeconds(10), passiveBreak: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);
    }

    [Fact]
    public void Idle_to_Working_on_HandleUnlock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, idleThreshold: TimeSpan.FromSeconds(10), passiveBreak: TimeSpan.FromSeconds(5));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.FromSeconds(10));
        Assert.Equal(WorkCyclePhase.Idle, tracker.CurrentPhase);

        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void PendingReminder_to_ReminderVisible_on_natural_pause()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_to_BreakInProgress_on_StartBreak()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.StartBreak();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakInProgress_to_Working_after_break_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5), breakDuration: TimeSpan.FromSeconds(20));
        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void BreakInProgress_to_PendingReminder_on_CancelBreak_when_debt_owed()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);
        tracker.StartBreak();

        tracker.CancelBreak();

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_to_Snoozed_on_Snooze()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.Snooze();

        Assert.Equal(WorkCyclePhase.Snoozed, tracker.CurrentPhase);
    }

    [Fact]
    public void Snoozed_to_PendingReminder_after_snooze_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5), snoozeDuration: TimeSpan.FromSeconds(10));
        ReachReminderVisible(tracker, clock);
        tracker.Snooze();

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    [Fact]
    public void ReminderVisible_Ignored_returns_to_Working()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30), naturalPause: TimeSpan.FromSeconds(5));
        ReachReminderVisible(tracker, clock);

        tracker.Ignore();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Pause_and_Resume_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Pause();
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);

        tracker.Resume();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void FocusMode_start_and_end()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.StartFocusMode();
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);

        tracker.EndFocusMode();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Disable_and_Enable_cycle()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.Disable();
        Assert.Equal(WorkCyclePhase.Disabled, tracker.CurrentPhase);

        tracker.Enable();
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void HandleLock_and_HandleUnlock()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.HandleLock();

        clock.Advance(TimeSpan.FromHours(1));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleUnlock();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void Sleep_does_not_accumulate_work_time()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        tracker.HandleSleep();

        clock.Advance(TimeSpan.FromHours(2));
        tracker.Tick(TimeSpan.Zero);

        var timeBeforeResume = tracker.AccumulatedWorkTime;
        tracker.HandleResume();

        Assert.Equal(timeBeforeResume, tracker.AccumulatedWorkTime);
    }

    [Fact]
    public void TickActivityUnavailable_does_not_change_phase()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.TickActivityUnavailable();

        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    [Fact]
    public void ManualStartBreak_enters_BreakInProgress()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);

        tracker.ManualStartBreak();

        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    /// <summary>
    /// A reminder held back to a tray cue inside a fullscreen application must not hijack
    /// the break the user then starts by hand, even when the context cap lifts mid-break.
    /// </summary>
    [Fact]
    public void Held_back_reminder_does_not_hijack_a_manually_started_break()
    {
        var clock = new FakeClock();
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

        int trayCues = 0;
        int reminderShown = 0;
        int breakStarted = 0;
        int breakCancelled = 0;
        int breakCompleted = 0;
        tracker.ReminderSuppressed += (_, e) => { if (e.ShowTrayCue) trayCues++; };
        tracker.ReminderShown += (_, _) => reminderShown++;
        tracker.BreakStarted += (_, _) => breakStarted++;
        tracker.BreakCancelled += (_, _) => breakCancelled++;
        tracker.BreakCompleted += (_, _) => breakCompleted++;

        // A fullscreen application caps presentation at tray-only.
        tracker.SetIntensityCaps(PresentationIntensity.TrayOnly, PresentationIntensity.PopupAndSound);

        // The user works past the reminder interval and on to rest-debt Level 3, where a
        // popup would normally be permitted. The attempt is held back to a tray cue.
        for (int i = 0; i < 21; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }

        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
        Assert.Equal(RestDebtLevel.Level3, tracker.RestDebtLevel);
        Assert.Equal(1, trayCues);
        Assert.Equal(0, reminderShown);

        // The user notices the tray cue and starts a break from the tray.
        tracker.ManualStartBreak();
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(1, breakStarted);

        // Mid-break they alt-tab out of the fullscreen application and the cap lifts.
        clock.Advance(TimeSpan.FromSeconds(5));
        tracker.Tick(TimeSpan.Zero);
        tracker.SetIntensityCaps(PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound);

        // The break survives, and no one is asked to start the break they are taking.
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
        Assert.Equal(0, reminderShown);

        for (int i = 0; i < 20 && tracker.CurrentPhase == WorkCyclePhase.BreakInProgress; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
            Assert.Equal(0, reminderShown);
        }

        Assert.Equal(1, breakCompleted);
        Assert.Equal(0, breakCancelled);
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
        Assert.Equal(TimeSpan.Zero, tracker.AccumulatedWorkTime);
        Assert.Equal(RestDebtLevel.Level0, tracker.RestDebtLevel);
    }

    private static void ReachPendingReminder(WorkCycleTracker tracker, FakeClock clock)
    {
        for (int i = 0; i < 31; i++)
        {
            tracker.Tick(TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(WorkCyclePhase.PendingReminder, tracker.CurrentPhase);
    }

    private static void ReachReminderVisible(WorkCycleTracker tracker, FakeClock clock)
    {
        ReachPendingReminder(tracker, clock);

        clock.Advance(TimeSpan.FromSeconds(6));
        tracker.Tick(TimeSpan.FromSeconds(6));

        Assert.Equal(WorkCyclePhase.ReminderVisible, tracker.CurrentPhase);
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock clock,
        TimeSpan? workInterval = null,
        TimeSpan? idleThreshold = null,
        TimeSpan? naturalPause = null,
        TimeSpan? maxWait = null,
        TimeSpan? breakDuration = null,
        TimeSpan? passiveBreak = null,
        TimeSpan? snoozeDuration = null,
        TimeSpan? reminderDisplay = null,
        TimeSpan? retryCooldown = null)
    {
        var tracker = new WorkCycleTracker(
            clock,
            workInterval ?? TimeSpan.FromSeconds(30),
            idleThreshold ?? DefaultIdleThreshold,
            naturalPause ?? DefaultNaturalPause,
            maxWait ?? DefaultMaxWait,
            breakDuration ?? DefaultBreakDuration,
            passiveBreak ?? DefaultPassiveBreak,
            snoozeDuration ?? DefaultSnoozeDuration,
            reminderDisplay ?? DefaultReminderDisplay,
            retryCooldown ?? DefaultRetryCooldown);
        tracker.SetForceAllowPopup(true);
        return tracker;
    }
}
