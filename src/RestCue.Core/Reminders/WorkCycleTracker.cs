using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Policies;
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
    private readonly TimeSpan retryCooldown;

    private DateTimeOffset? pendingSinceUtc;
    private DateTimeOffset? breakStartUtc;
    private DateTimeOffset? lastTickUtc;
    private DateTimeOffset? reminderVisibleSinceUtc;
    private DateTimeOffset? snoozeUntilUtc;
    private DateTimeOffset? cooldownUntil;
    private DateTimeOffset? nextDebtDeadline;
    private bool wasWorking;
    private bool isLocked;
    private bool isSleeping;
    private bool wasPassivePaused;

    private readonly TimeSpan workInterval;
    private readonly TimeSpan debtLevel2Threshold;
    private readonly TimeSpan debtLevel3Threshold;
    private readonly TimeSpan debtLevel4Threshold;
    private TimeSpan effectiveWorkInterval;

    private RestDebtLevel restDebtLevel;

    private bool isFullscreen;
    private bool isReminderSuppressed;
    private bool hasSuppressedReminder;
    private bool showTrayCue;

    private PresentationIntensity _contextCap = PresentationIntensityPolicy.DefaultContextCap;
    private PresentationIntensity _userCap = PresentationIntensityPolicy.DefaultUserCap;
    private bool _forceAllowPopup;

    public WorkCycleTracker(
        IClock clock,
        TimeSpan workInterval,
        TimeSpan idleThreshold,
        TimeSpan naturalPauseThreshold,
        TimeSpan maximumReminderWait,
        TimeSpan breakDuration,
        TimeSpan passiveBreakThreshold,
        TimeSpan snoozeDuration,
        TimeSpan reminderDisplayDuration,
        TimeSpan retryCooldown,
        TimeSpan debtLevel2 = default,
        TimeSpan debtLevel3 = default,
        TimeSpan debtLevel4 = default)
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
        ValidateThreshold(retryCooldown, nameof(retryCooldown));

        if (passiveBreakThreshold >= idleThreshold)
            throw new ArgumentOutOfRangeException(
                nameof(passiveBreakThreshold), passiveBreakThreshold,
                "Passive break threshold must be less than idle threshold.");

        var l2 = debtLevel2 == default ? TimeSpan.FromMinutes(35) : debtLevel2;
        var l3 = debtLevel3 == default ? TimeSpan.FromMinutes(45) : debtLevel3;
        var l4 = debtLevel4 == default ? TimeSpan.FromMinutes(60) : debtLevel4;

        DebtPolicy.ValidateThresholds(workInterval, l2, l3, l4);

        this.clock = clock;
        this.workInterval = workInterval;
        this.effectiveWorkInterval = workInterval;
        this.debtLevel2Threshold = l2;
        this.debtLevel3Threshold = l3;
        this.debtLevel4Threshold = l4;
        this.idleThreshold = idleThreshold;
        this.naturalPauseThreshold = naturalPauseThreshold;
        this.maximumReminderWait = maximumReminderWait;
        this.breakDuration = breakDuration;
        this.passiveBreakThreshold = passiveBreakThreshold;
        this.snoozeDuration = snoozeDuration;
        this.reminderDisplayDuration = reminderDisplayDuration;
        this.retryCooldown = retryCooldown;
    }

    public WorkCyclePhase CurrentPhase { get; private set; } = WorkCyclePhase.Working;

    public TimeSpan AccumulatedWorkTime { get; private set; }

    public TimeSpan BreakDuration => breakDuration;

    public TimeSpan SnoozeDuration => snoozeDuration;

    public TimeSpan RetryCooldown => retryCooldown;

    public DateTimeOffset? CooldownUntil => cooldownUntil;

    public RestDebtLevel RestDebtLevel => restDebtLevel;

    public event EventHandler<RestDebtLevelChangedEventArgs>? RestDebtLevelChanged;

    public void SetNextDebtDeadline(DateTimeOffset? deadline)
    {
        nextDebtDeadline = cooldownUntil.HasValue ? deadline : null;
    }

    public void SetForceAllowPopup(bool force)
    {
        _forceAllowPopup = force;
    }

    public void SetIntensityCaps(PresentationIntensity contextCap, PresentationIntensity userCap)
    {
        var oldEffective = GetEffectiveIntensity();
        _contextCap = Enum.IsDefined(contextCap) ? contextCap : PresentationIntensityPolicy.DefaultContextCap;
        _userCap = Enum.IsDefined(userCap) ? userCap : PresentationIntensityPolicy.DefaultUserCap;

        if (hasSuppressedReminder)
        {
            var newEffective = GetEffectiveIntensity();
            if (newEffective >= PresentationIntensity.EdgePopup && oldEffective < PresentationIntensity.EdgePopup)
            {
                hasSuppressedReminder = false;
                showTrayCue = false;
                EnterReminderVisible(clock.UtcNow);
            }
            else
            {
                bool oldCue = oldEffective >= PresentationIntensity.TrayOnly;
                bool newCue = newEffective >= PresentationIntensity.TrayOnly;
                if (newCue != oldCue)
                {
                    showTrayCue = newCue;
                    ReminderSuppressed?.Invoke(this, new ReminderSuppressedEventArgs(newCue));
                }
            }
        }
    }

    private PresentationIntensity GetEffectiveIntensity()
    {
        var debtRec = PresentationIntensityPolicy.GetDebtRecommendation(restDebtLevel);
        return PresentationIntensityPolicy.Effective(debtRec, _contextCap, _userCap);
    }

    public event EventHandler? ReminderShown;
    public event EventHandler? BreakStarted;
    public event EventHandler? BreakCompleted;
    public event EventHandler? BreakCancelled;
    public event EventHandler? PassivePauseDetected;
    public event EventHandler<ReminderDismissedEventArgs>? ReminderDismissed;
    public event EventHandler? IdleStarted;
    public event EventHandler? IdleEnded;
    public event EventHandler? CooldownStarted;
    public event EventHandler? CooldownEnded;
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
            EvaluateDebtLevel();
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
                if (!isWorking)
                {
                    EnterIdle();
                }
                break;
        }
    }

    private void TickIdle(DateTimeOffset now, bool isWorking)
    {
        if (isWorking)
        {
            IdleEnded?.Invoke(this, EventArgs.Empty);
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
        BreakStarted?.Invoke(this, EventArgs.Empty);
    }

    public void ManualStartBreak()
    {
        if (CurrentPhase is not (WorkCyclePhase.Working or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed
            or WorkCyclePhase.FocusMode))
            throw new InvalidOperationException(
                $"Cannot start break from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.BreakInProgress;
        breakStartUtc = clock.UtcNow;
        pendingSinceUtc = null;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        wasPassivePaused = false;
        BreakStarted?.Invoke(this, EventArgs.Empty);
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

        CurrentPhase = WorkCyclePhase.Working;
        pendingSinceUtc = null;
        breakStartUtc = null;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        cooldownUntil = clock.UtcNow + retryCooldown;
        CooldownStarted?.Invoke(this, EventArgs.Empty);
        ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.Ignored));
    }

    public void Pause()
    {
        if (CurrentPhase is not (WorkCyclePhase.Working or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed))
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

        lastTickUtc = null;
        wasWorking = false;
        CurrentPhase = WorkCyclePhase.Working;
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

        if (TryEnterPendingReminderFromWorking(clock.UtcNow))
        {
            FocusModeEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentPhase = WorkCyclePhase.Working;
        FocusModeEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Disable()
    {
        if (CurrentPhase == WorkCyclePhase.Disabled)
            throw new InvalidOperationException("Already disabled.");

        bool wasCooldownActive = cooldownUntil.HasValue;
        CurrentPhase = WorkCyclePhase.Disabled;
        ClearReminderState();
        cooldownUntil = null;
        nextDebtDeadline = null;
        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);
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
                if (CurrentPhase == WorkCyclePhase.BreakInProgress)
                    BreakCancelled?.Invoke(this, EventArgs.Empty);
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
                if (CurrentPhase == WorkCyclePhase.BreakInProgress)
                    BreakCancelled?.Invoke(this, EventArgs.Empty);
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

        TryEnterPendingReminderFromWorking(now);
    }

    private bool TryEnterPendingReminderFromWorking(DateTimeOffset now)
    {
        if (cooldownUntil.HasValue)
        {
            DateTimeOffset effective = EarlierOf(cooldownUntil, nextDebtDeadline)!.Value;
            if (now < effective)
                return false;

            bool wasDebtDeadline = nextDebtDeadline.HasValue && effective == nextDebtDeadline.Value;

            cooldownUntil = null;
            nextDebtDeadline = null;
            CooldownEnded?.Invoke(this, EventArgs.Empty);

            if (wasDebtDeadline)
            {
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSinceUtc = now;
                return true;
            }

            if (effectiveWorkInterval > TimeSpan.Zero &&
                AccumulatedWorkTime >= effectiveWorkInterval)
            {
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSinceUtc = now;
                return true;
            }

            return false;
        }

        if (effectiveWorkInterval > TimeSpan.Zero &&
            AccumulatedWorkTime >= effectiveWorkInterval)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSinceUtc = now;
            return true;
        }

        return false;
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
        var wasCooldownActive = cooldownUntil.HasValue;
        var wasInBreak = CurrentPhase == WorkCyclePhase.BreakInProgress;
        var previousLevel = restDebtLevel;

        CurrentPhase = WorkCyclePhase.Idle;
        AccumulatedWorkTime = TimeSpan.Zero;
        pendingSinceUtc = null;
        breakStartUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        wasPassivePaused = false;
        cooldownUntil = null;
        nextDebtDeadline = null;
        restDebtLevel = RestDebtLevel.Level0;

        if (wasInBreak)
            BreakCancelled?.Invoke(this, EventArgs.Empty);
        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);
        IdleStarted?.Invoke(this, EventArgs.Empty);

        if (previousLevel != RestDebtLevel.Level0)
            RestDebtLevelChanged?.Invoke(this, new RestDebtLevelChangedEventArgs(previousLevel, RestDebtLevel.Level0));
    }

    private void EnterReminderVisible(DateTimeOffset now)
    {
        bool wasCooldownActive = cooldownUntil.HasValue;
        cooldownUntil = null;
        nextDebtDeadline = null;
        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);

        if (!_forceAllowPopup)
        {
            var effective = GetEffectiveIntensity();

            if (effective < PresentationIntensity.EdgePopup)
            {
                bool showCue = effective >= PresentationIntensity.TrayOnly;
                if (!hasSuppressedReminder || showTrayCue != showCue)
                {
                    showTrayCue = showCue;
                    hasSuppressedReminder = true;
                    ReminderSuppressed?.Invoke(this, new ReminderSuppressedEventArgs(showCue));
                }
                return;
            }
        }

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
            CurrentPhase = WorkCyclePhase.Working;
            pendingSinceUtc = null;
            breakStartUtc = null;
            lastTickUtc = null;
            wasWorking = false;
            reminderVisibleSinceUtc = null;
            snoozeUntilUtc = null;
            cooldownUntil = now + retryCooldown;
            CooldownStarted?.Invoke(this, EventArgs.Empty);
            ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.AutoDismissed));
        }
    }

    private void ResetCycle()
    {
        var previousLevel = restDebtLevel;
        bool wasCooldownActive = cooldownUntil.HasValue;
        CurrentPhase = WorkCyclePhase.Working;
        AccumulatedWorkTime = TimeSpan.Zero;
        pendingSinceUtc = null;
        breakStartUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        wasPassivePaused = false;
        isReminderSuppressed = false;
        hasSuppressedReminder = false;
        showTrayCue = false;
        cooldownUntil = null;
        nextDebtDeadline = null;
        restDebtLevel = RestDebtLevel.Level0;

        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);

        if (previousLevel != RestDebtLevel.Level0)
            RestDebtLevelChanged?.Invoke(this, new RestDebtLevelChangedEventArgs(previousLevel, RestDebtLevel.Level0));
    }

    private void EvaluateDebtLevel()
    {
        var newLevel = DebtPolicy.Evaluate(
            AccumulatedWorkTime,
            workInterval,
            debtLevel2Threshold,
            debtLevel3Threshold,
            debtLevel4Threshold);

        if (newLevel != restDebtLevel)
        {
            var previous = restDebtLevel;
            restDebtLevel = newLevel;
            RestDebtLevelChanged?.Invoke(this, new RestDebtLevelChangedEventArgs(previous, newLevel));
            UpdateDebtDeadline();
        }
    }

    private void UpdateDebtDeadline()
    {
        if (!cooldownUntil.HasValue)
            return;

        var nextThreshold = DebtPolicy.GetNextThreshold(
            restDebtLevel,
            workInterval,
            debtLevel2Threshold,
            debtLevel3Threshold,
            debtLevel4Threshold);

        if (nextThreshold.HasValue)
        {
            var remaining = nextThreshold.Value - AccumulatedWorkTime;
            if (remaining > TimeSpan.Zero)
                SetNextDebtDeadline(clock.UtcNow + remaining);
            else
                SetNextDebtDeadline(clock.UtcNow);
        }
        else
        {
            SetNextDebtDeadline(null);
        }
    }

    private void ClearReminderState()
    {
        pendingSinceUtc = null;
        breakStartUtc = null;
        reminderVisibleSinceUtc = null;
        snoozeUntilUtc = null;
        lastTickUtc = null;
        wasWorking = false;
        wasPassivePaused = false;
        hasSuppressedReminder = false;
    }

    private static DateTimeOffset? EarlierOf(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return a.Value < b.Value ? a : b;
    }

    private static void ValidateThreshold(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName, value, "Threshold must be positive.");
    }
}
