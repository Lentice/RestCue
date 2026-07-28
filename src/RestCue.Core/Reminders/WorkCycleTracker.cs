using RestCue.Core.Time;

namespace RestCue.Core.Reminders;

public sealed class WorkCycleTracker
{
    private readonly IClock clock;
    private readonly TimeSpan idleThreshold;
    private readonly TimeSpan naturalPauseThreshold;
    private readonly TimeSpan maximumReminderWait;
    private readonly TimeSpan breakDuration;
    private readonly TimeSpan passiveBreakThreshold;
    private readonly TimeSpan snoozeDuration;
    private readonly TimeSpan reminderDisplayDuration;

    private DateTimeOffset? pendingSinceUtc;
    private DateTimeOffset? breakStartUtc;
    private DateTimeOffset? lastTickUtc;
    private DateTimeOffset? reminderVisibleSinceUtc;
    private DateTimeOffset? snoozeUntilUtc;
    private bool wasWorking;
    private TimeSpan? lastReminderWorkTime;
    private bool isLocked;
    private bool isSleeping;
    private bool wasPassivePaused;

    private readonly TimeSpan workInterval;
    private TimeSpan effectiveWorkInterval;

    private bool isFullscreen;
    private bool isReminderSuppressed;
    private bool hasSuppressedReminder;
    private bool showTrayCue;

    public WorkCycleTracker(
        IClock clock,
        TimeSpan workInterval,
        TimeSpan idleThreshold,
        TimeSpan naturalPauseThreshold,
        TimeSpan maximumReminderWait,
        TimeSpan breakDuration,
        TimeSpan passiveBreakThreshold,
        TimeSpan snoozeDuration,
        TimeSpan reminderDisplayDuration)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateThreshold(workInterval, nameof(workInterval));
        ValidateThreshold(idleThreshold, nameof(idleThreshold));
        ValidateThreshold(naturalPauseThreshold, nameof(naturalPauseThreshold));
        ValidateThreshold(maximumReminderWait, nameof(maximumReminderWait));
        ValidateThreshold(breakDuration, nameof(breakDuration));
        ValidateThreshold(passiveBreakThreshold, nameof(passiveBreakThreshold));
        ValidateThreshold(snoozeDuration, nameof(snoozeDuration));
        ValidateThreshold(reminderDisplayDuration, nameof(reminderDisplayDuration));

        if (passiveBreakThreshold >= idleThreshold)
            throw new ArgumentOutOfRangeException(
                nameof(passiveBreakThreshold), passiveBreakThreshold,
                "Passive break threshold must be less than idle threshold.");

        this.clock = clock;
        this.workInterval = workInterval;
        this.effectiveWorkInterval = workInterval;
        this.idleThreshold = idleThreshold;
        this.naturalPauseThreshold = naturalPauseThreshold;
        this.maximumReminderWait = maximumReminderWait;
        this.breakDuration = breakDuration;
        this.passiveBreakThreshold = passiveBreakThreshold;
        this.snoozeDuration = snoozeDuration;
        this.reminderDisplayDuration = reminderDisplayDuration;
    }

    public WorkCyclePhase CurrentPhase { get; private set; } = WorkCyclePhase.Working;

    public TimeSpan AccumulatedWorkTime { get; private set; }

    public TimeSpan BreakDuration => breakDuration;

    public TimeSpan SnoozeDuration => snoozeDuration;

    public event EventHandler? ReminderShown;
    public event EventHandler? BreakCompleted;
    public event EventHandler? PassivePauseDetected;
    public event EventHandler<ReminderDismissedEventArgs>? ReminderDismissed;
    public event EventHandler? Paused;
    public event EventHandler? Resumed;
    public event EventHandler? FocusModeStarted;
    public event EventHandler? FocusModeEnded;
    public event EventHandler? Disabled;
    public event EventHandler? Enabled;
    public event EventHandler<ReminderSuppressedEventArgs>? ReminderSuppressed;

    public void Tick(TimeSpan idleDuration)
    {
        if (isLocked || isSleeping)
            return;

        if (idleDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleDuration), "Idle duration cannot be negative.");

        var now = clock.UtcNow;
        bool isWorking = idleDuration < idleThreshold;

        if (CurrentPhase != WorkCyclePhase.BreakInProgress &&
            CurrentPhase != WorkCyclePhase.Paused &&
            CurrentPhase != WorkCyclePhase.Disabled)
        {
            AccumulateIfWorking(now, isWorking);
        }

        switch (CurrentPhase)
        {
            case WorkCyclePhase.Working:
                TickWorking(now, isWorking);
                break;

            case WorkCyclePhase.PendingReminder:
                TickPending(now, isWorking, idleDuration);
                break;

            case WorkCyclePhase.ReminderVisible:
                TickReminderVisible(now, idleDuration);
                break;

            case WorkCyclePhase.BreakInProgress:
                TickBreak(now);
                break;

            case WorkCyclePhase.Snoozed:
                TickSnoozed(now, isWorking, idleDuration);
                break;

            case WorkCyclePhase.Idle:
                TickIdle(now, isWorking);
                break;

            case WorkCyclePhase.Paused:
            case WorkCyclePhase.Disabled:
                break;

            case WorkCyclePhase.FocusMode:
                break;
        }
    }

    private void TickIdle(DateTimeOffset now, bool isWorking)
    {
        if (isWorking)
        {
            ResetCycle();
        }
    }

    public void TickActivityUnavailable()
    {
        if (isLocked || isSleeping)
            return;

        var now = clock.UtcNow;
        wasWorking = false;
        lastTickUtc = now;

        switch (CurrentPhase)
        {
            case WorkCyclePhase.Working:
            case WorkCyclePhase.FocusMode:
                break;

            case WorkCyclePhase.PendingReminder:
                if (now - pendingSinceUtc!.Value >= maximumReminderWait)
                {
                    EnterReminderVisible(now);
                }
                break;

            case WorkCyclePhase.ReminderVisible:
                TryAutoDismiss(now);
                break;

            case WorkCyclePhase.BreakInProgress:
                if (now - breakStartUtc!.Value >= breakDuration)
                {
                    ResetCycle();
                    BreakCompleted?.Invoke(this, EventArgs.Empty);
                }
                break;

            case WorkCyclePhase.Snoozed:
                if (now >= snoozeUntilUtc!.Value)
                {
                    CurrentPhase = WorkCyclePhase.PendingReminder;
                    pendingSinceUtc = now;
                    snoozeUntilUtc = null;
                }
                break;

            case WorkCyclePhase.Idle:
            case WorkCyclePhase.Paused:
            case WorkCyclePhase.Disabled:
                break;
        }
    }

    public void StartBreak()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot start break from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.BreakInProgress;
        breakStartUtc = clock.UtcNow;
        reminderVisibleSinceUtc = null;
    }

    public void Snooze()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot snooze from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.Snoozed;
        snoozeUntilUtc = clock.UtcNow + snoozeDuration;
        reminderVisibleSinceUtc = null;
        ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.Snoozed));
    }

    public void Ignore()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot ignore from phase {CurrentPhase}.");

        lastReminderWorkTime = AccumulatedWorkTime;
        CurrentPhase = WorkCyclePhase.Working;
        pendingSinceUtc = null;
        breakStartUtc = null;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.Ignored));
    }

    public void Pause()
    {
        if (CurrentPhase is WorkCyclePhase.Paused or WorkCyclePhase.FocusMode or WorkCyclePhase.Disabled or WorkCyclePhase.BreakInProgress)
            throw new InvalidOperationException(
                $"Cannot pause from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.Paused;
        ClearReminderState();
        Paused?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (CurrentPhase != WorkCyclePhase.Paused)
            throw new InvalidOperationException(
                $"Cannot resume from phase {CurrentPhase}.");

        ResetCycle();
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    public void StartFocusMode()
    {
        if (CurrentPhase is WorkCyclePhase.Paused or WorkCyclePhase.FocusMode or WorkCyclePhase.Disabled or WorkCyclePhase.BreakInProgress or WorkCyclePhase.Idle)
            throw new InvalidOperationException(
                $"Cannot start focus mode from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.FocusMode;
        ClearReminderState();
        FocusModeStarted?.Invoke(this, EventArgs.Empty);
    }

    public void EndFocusMode()
    {
        if (CurrentPhase != WorkCyclePhase.FocusMode)
            throw new InvalidOperationException(
                $"Cannot end focus mode from phase {CurrentPhase}.");

        if (AccumulatedWorkTime - (lastReminderWorkTime ?? TimeSpan.Zero) >= effectiveWorkInterval)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSinceUtc = clock.UtcNow;
        }
        else
        {
            CurrentPhase = WorkCyclePhase.Working;
        }

        FocusModeEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Disable()
    {
        if (CurrentPhase == WorkCyclePhase.Disabled)
            throw new InvalidOperationException("Already disabled.");

        CurrentPhase = WorkCyclePhase.Disabled;
        ClearReminderState();
        Disabled?.Invoke(this, EventArgs.Empty);
    }

    public void Enable()
    {
        if (CurrentPhase != WorkCyclePhase.Disabled)
            throw new InvalidOperationException(
                $"Cannot enable from phase {CurrentPhase}.");

        ResetCycle();
        Enabled?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateForegroundContext(bool fullscreen, bool suppressReminder, bool showTrayCue, TimeSpan? effectiveWorkIntervalOverride)
    {
        bool wasSuppressed = isReminderSuppressed;
        bool previousShowTrayCue = this.showTrayCue;
        isFullscreen = fullscreen;
        isReminderSuppressed = fullscreen || suppressReminder;
        this.showTrayCue = showTrayCue;

        if (effectiveWorkIntervalOverride.HasValue)
            effectiveWorkInterval = effectiveWorkIntervalOverride.Value;
        else
            effectiveWorkInterval = workInterval;

        if (wasSuppressed && !isReminderSuppressed && hasSuppressedReminder && CurrentPhase == WorkCyclePhase.PendingReminder)
        {
            var now = clock.UtcNow;
            hasSuppressedReminder = false;
            EnterReminderVisible(now);
        }
        else if (hasSuppressedReminder && isReminderSuppressed && this.showTrayCue != previousShowTrayCue
                 && CurrentPhase == WorkCyclePhase.PendingReminder)
        {
            ReminderSuppressed?.Invoke(this, new ReminderSuppressedEventArgs(this.showTrayCue));
        }
    }

    public void HandleLock()
    {
        isLocked = true;

        if (CurrentPhase is WorkCyclePhase.PendingReminder or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed
            or WorkCyclePhase.Working or WorkCyclePhase.FocusMode)
        {
            ResetCycle();
        }
    }

    public void HandleUnlock()
    {
        isLocked = false;

        if (!isSleeping)
        {
            if (CurrentPhase is WorkCyclePhase.Paused or WorkCyclePhase.Disabled)
            {
                ClearReminderState();
            }
            else
            {
                ResetCycle();
            }
        }
    }

    public void HandleSleep()
    {
        isSleeping = true;

        if (CurrentPhase is WorkCyclePhase.PendingReminder or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed
            or WorkCyclePhase.Working or WorkCyclePhase.FocusMode)
        {
            ResetCycle();
        }
    }

    public void HandleResume()
    {
        isSleeping = false;

        if (!isLocked)
        {
            if (CurrentPhase is WorkCyclePhase.Paused or WorkCyclePhase.Disabled)
            {
                ClearReminderState();
            }
            else
            {
                ResetCycle();
            }
        }
    }

    private void AccumulateIfWorking(DateTimeOffset now, bool isWorking)
    {
        if (isWorking && wasWorking && lastTickUtc.HasValue)
        {
            var delta = now - lastTickUtc.Value;
            if (delta > TimeSpan.Zero)
                AccumulatedWorkTime += delta;
        }

        wasWorking = isWorking;
        lastTickUtc = now;
    }

    private void TickWorking(DateTimeOffset now, bool isWorking)
    {
        if (!isWorking)
        {
            EnterIdle();
            return;
        }

        if (isWorking &&
            effectiveWorkInterval > TimeSpan.Zero &&
            AccumulatedWorkTime - (lastReminderWorkTime ?? TimeSpan.Zero) >= effectiveWorkInterval)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSinceUtc = now;
        }
    }

    private void TickPending(DateTimeOffset now, bool isWorking, TimeSpan idleDuration)
    {
        if (idleDuration >= idleThreshold)
        {
            EnterIdle();
            return;
        }

        if (idleDuration >= passiveBreakThreshold)
        {
            if (!wasPassivePaused)
            {
                wasPassivePaused = true;
                PassivePauseDetected?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (wasPassivePaused)
        {
            pendingSinceUtc = now;
            wasPassivePaused = false;
        }

        var elapsed = now - pendingSinceUtc!.Value;

        if (idleDuration >= naturalPauseThreshold)
        {
            EnterReminderVisible(now);
            return;
        }

        if (elapsed >= maximumReminderWait)
        {
            EnterReminderVisible(now);
        }
    }

    private void TickReminderVisible(DateTimeOffset now, TimeSpan idleDuration)
    {
        if (idleDuration >= idleThreshold)
        {
            EnterIdle();
            return;
        }

        if (idleDuration >= passiveBreakThreshold)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSinceUtc = now;
            reminderVisibleSinceUtc = null;
            wasPassivePaused = true;
            PassivePauseDetected?.Invoke(this, EventArgs.Empty);
            return;
        }

        TryAutoDismiss(now);
    }

    private void TickSnoozed(DateTimeOffset now, bool isWorking, TimeSpan idleDuration)
    {
        if (!isWorking && idleDuration >= idleThreshold)
        {
            EnterIdle();
            return;
        }

        if (now >= snoozeUntilUtc!.Value)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSinceUtc = now;
            snoozeUntilUtc = null;
        }
    }

    private void TickBreak(DateTimeOffset now)
    {
        if (now - breakStartUtc!.Value >= breakDuration)
        {
            ResetCycle();
            BreakCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnterIdle()
    {
        CurrentPhase = WorkCyclePhase.Idle;
        AccumulatedWorkTime = TimeSpan.Zero;
        pendingSinceUtc = null;
        breakStartUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastReminderWorkTime = null;
        wasPassivePaused = false;
    }

    private void EnterReminderVisible(DateTimeOffset now)
    {
        lastReminderWorkTime = AccumulatedWorkTime;

        if (isReminderSuppressed)
        {
            if (!hasSuppressedReminder)
            {
                hasSuppressedReminder = true;
                ReminderSuppressed?.Invoke(this, new ReminderSuppressedEventArgs(showTrayCue));
            }
            return;
        }

        CurrentPhase = WorkCyclePhase.ReminderVisible;
        reminderVisibleSinceUtc = now;
        ReminderShown?.Invoke(this, EventArgs.Empty);
    }

    private void TryAutoDismiss(DateTimeOffset now)
    {
        var visibleElapsed = now - reminderVisibleSinceUtc!.Value;
        if (visibleElapsed >= reminderDisplayDuration)
        {
            lastReminderWorkTime = AccumulatedWorkTime;
            CurrentPhase = WorkCyclePhase.Working;
            pendingSinceUtc = null;
            breakStartUtc = null;
            lastTickUtc = null;
            wasWorking = false;
            reminderVisibleSinceUtc = null;
            snoozeUntilUtc = null;
            ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.AutoDismissed));
        }
    }

    private void ResetCycle()
    {
        CurrentPhase = WorkCyclePhase.Working;
        AccumulatedWorkTime = TimeSpan.Zero;
        pendingSinceUtc = null;
        breakStartUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastReminderWorkTime = null;
        wasPassivePaused = false;
        isReminderSuppressed = false;
        hasSuppressedReminder = false;
        showTrayCue = false;
    }

    private void ClearReminderState()
    {
        pendingSinceUtc = null;
        breakStartUtc = null;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        lastReminderWorkTime = null;
        wasPassivePaused = false;
    }

    private static void ValidateThreshold(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName, value, "Threshold must be positive.");
    }
}
