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
    private TimeSpan snoozeDuration;
    private readonly TimeSpan reminderDisplayDuration;
    private readonly TimeSpan retryCooldown;
    private readonly TimeSpan focusModeDuration;

    private TimeSpan? pendingSince;
    private TimeSpan? breakStart;
    private TimeSpan? lastTick;
    private TimeSpan? reminderVisibleSince;
    private TimeSpan? snoozeUntil;
    private TimeSpan? cooldownUntil;
    private TimeSpan? nextDebtDeadline;
    private TimeSpan? focusModeUntil;
    private TimeSpan? pauseUntil;
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

    private PresentationIntensity contextCap = PresentationIntensityPolicy.DefaultContextCap;
    private PresentationIntensity userCap = PresentationIntensityPolicy.DefaultUserCap;
    private bool forceAllowPopup;

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
        TimeSpan debtLevel4 = default,
        TimeSpan focusModeDuration = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateThreshold(workInterval, nameof(workInterval));
        ValidateThreshold(idleThreshold, nameof(idleThreshold));
        ValidateThreshold(naturalPauseThreshold, nameof(naturalPauseThreshold));
        // The settings validator accepts a maximum reminder wait of 0–10 minutes, where
        // zero means "Timing is eligible as soon as a reminder is pending". The guard
        // yields to the validator: only a negative wait is nonsense.
        ValidateNonNegative(maximumReminderWait, nameof(maximumReminderWait));
        ValidateThreshold(breakDuration, nameof(breakDuration));
        ValidateThreshold(passiveBreakThreshold, nameof(passiveBreakThreshold));
        ValidateThreshold(snoozeDuration, nameof(snoozeDuration));
        ValidateThreshold(reminderDisplayDuration, nameof(reminderDisplayDuration));
        ValidateThreshold(retryCooldown, nameof(retryCooldown));

        var effectiveFocusModeDuration = focusModeDuration == default ? TimeSpan.FromMinutes(60) : focusModeDuration;
        ValidateThreshold(effectiveFocusModeDuration, nameof(focusModeDuration));

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
        effectiveWorkInterval = workInterval;
        debtLevel2Threshold = l2;
        debtLevel3Threshold = l3;
        debtLevel4Threshold = l4;
        this.idleThreshold = idleThreshold;
        this.naturalPauseThreshold = naturalPauseThreshold;
        this.maximumReminderWait = maximumReminderWait;
        this.breakDuration = breakDuration;
        this.passiveBreakThreshold = passiveBreakThreshold;
        this.snoozeDuration = snoozeDuration;
        this.reminderDisplayDuration = reminderDisplayDuration;
        this.retryCooldown = retryCooldown;
        this.focusModeDuration = effectiveFocusModeDuration;
    }

    public WorkCyclePhase CurrentPhase { get; private set; } = WorkCyclePhase.Working;

    public TimeSpan AccumulatedWorkTime { get; private set; }

    /// <summary>
    /// Time until the engine next needs a rest, or <c>null</c> when the current phase has
    /// no active work clock. This is the time until a reminder becomes pending; the
    /// reminder may still wait for a natural pause before it is shown.
    /// </summary>
    public TimeSpan? TimeUntilNextRestNeed
    {
        get
        {
            if (CurrentPhase is not (WorkCyclePhase.Working or WorkCyclePhase.FocusMode))
                return null;

            var deadline = EarlierOf(cooldownUntil, nextDebtDeadline);
            if (deadline.HasValue)
            {
                var remaining = deadline.Value - clock.Elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            var thresholdRemaining = effectiveWorkInterval - AccumulatedWorkTime;
            return thresholdRemaining > TimeSpan.Zero ? thresholdRemaining : TimeSpan.Zero;
        }
    }

    public TimeSpan BreakDuration => breakDuration;

    public TimeSpan SnoozeDuration => snoozeDuration;

    public TimeSpan RetryCooldown => retryCooldown;

    public TimeSpan? CooldownUntil => cooldownUntil;

    public RestDebtLevel RestDebtLevel => restDebtLevel;

    public event EventHandler<RestDebtLevelChangedEventArgs>? RestDebtLevelChanged;

    public PresentationIntensity EffectiveIntensity { get; private set; } = PresentationIntensity.PopupAndSound;

    public void SetNextDebtDeadline(TimeSpan? deadline)
    {
        nextDebtDeadline = cooldownUntil.HasValue ? deadline : null;
    }

    /// <summary>
    /// Updates the snooze duration in place.
    /// </summary>
    /// <remarks>
    /// Snooze duration is the one engine timing value that can change without a rebuild:
    /// it is read only when <see cref="Snooze"/> computes a deadline, so it holds no
    /// accumulated state. A snooze already running keeps the deadline it was given; the new
    /// value applies to the next one. This is what lets the setting apply on save without
    /// the trusted reset that rebuilding the engine would inflict.
    /// </remarks>
    public void UpdateSnoozeDuration(TimeSpan duration)
    {
        ValidateThreshold(duration, nameof(duration));
        snoozeDuration = duration;
    }

    public void SetForceAllowPopup(bool force)
    {
        forceAllowPopup = force;
    }

    public void SetIntensityCaps(PresentationIntensity contextCap, PresentationIntensity userCap)
    {
        var oldEffective = GetEffectiveIntensity();
        this.contextCap = Enum.IsDefined(contextCap) ? contextCap : PresentationIntensityPolicy.DefaultContextCap;
        this.userCap = Enum.IsDefined(userCap) ? userCap : PresentationIntensityPolicy.DefaultUserCap;

        if (hasSuppressedReminder)
        {
            var newEffective = GetEffectiveIntensity();
            if (newEffective >= PresentationIntensity.EdgePopup)
            {
                // Same primary-phase guard as UpdateForegroundContext: a held-back
                // attempt may only be promoted while one is still pending. From any
                // other phase — a break above all — a cap change is bookkeeping only.
                if (CurrentPhase == WorkCyclePhase.PendingReminder)
                {
                    hasSuppressedReminder = false;
                    showTrayCue = false;
                    EnterReminderVisible(clock.Elapsed);
                }
            }
            else if (newEffective != oldEffective)
            {
                if (newEffective == PresentationIntensity.LightTouch)
                {
                    showTrayCue = true;
                    hasSuppressedReminder = true;
                    ReminderLightTouch?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    bool newCue = newEffective >= PresentationIntensity.TrayOnly;
                    showTrayCue = newCue;
                    ReminderSuppressed?.Invoke(this, new ReminderSuppressedEventArgs(newCue));
                }
            }
        }
    }

    private PresentationIntensity GetEffectiveIntensity()
    {
        var debtRec = PresentationIntensityPolicy.GetDebtRecommendation(restDebtLevel);
        var result = PresentationIntensityPolicy.Effective(debtRec, contextCap, userCap);
        EffectiveIntensity = result;
        return result;
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
    public event EventHandler? ReminderLightTouch;

    public event EventHandler<string?>? ProcessNameChanged;

    private string? lastProcessName;

    public void TrackForegroundProcess(string? processName)
    {
        if (string.Equals(lastProcessName, processName, StringComparison.OrdinalIgnoreCase))
            return;
        lastProcessName = processName;
        ProcessNameChanged?.Invoke(this, processName);
    }

    public void Tick(TimeSpan idleDuration)
    {
        if (isLocked || isSleeping)
            return;

        if (idleDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleDuration), "Idle duration cannot be negative.");

        var now = clock.Elapsed;
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
                if (pauseUntil.HasValue && now >= pauseUntil.Value)
                {
                    Resume();
                }
                break;
            case WorkCyclePhase.Disabled:
                break;

            case WorkCyclePhase.FocusMode:
                if (focusModeUntil.HasValue && now >= focusModeUntil.Value)
                {
                    EndFocusMode();
                }
                else if (!isWorking)
                {
                    ExitFocusMode();
                    EnterIdle();
                }
                break;
        }
    }

    private void TickIdle(TimeSpan now, bool isWorking)
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

        var now = clock.Elapsed;
        wasWorking = false;
        lastTick = now;

        switch (CurrentPhase)
        {
            case WorkCyclePhase.Working:
                break;
            case WorkCyclePhase.FocusMode:
                if (focusModeUntil.HasValue && now >= focusModeUntil.Value)
                {
                    EndFocusMode();
                }
                break;

            case WorkCyclePhase.PendingReminder:
                if (now - pendingSince!.Value >= maximumReminderWait)
                {
                    EnterReminderVisible(now);
                }
                break;

            case WorkCyclePhase.ReminderVisible:
                TryAutoDismiss(now);
                break;

            case WorkCyclePhase.BreakInProgress:
                if (now - breakStart!.Value >= breakDuration)
                {
                    ResetCycle();
                    BreakCompleted?.Invoke(this, EventArgs.Empty);
                }
                break;

            case WorkCyclePhase.Snoozed:
                if (now >= snoozeUntil!.Value)
                {
                    CurrentPhase = WorkCyclePhase.PendingReminder;
                    pendingSince = now;
                    snoozeUntil = null;
                }
                break;

            case WorkCyclePhase.Idle:
            case WorkCyclePhase.Disabled:
                break;

            case WorkCyclePhase.Paused:
                if (pauseUntil.HasValue && now >= pauseUntil.Value)
                {
                    Resume();
                }
                break;
        }
    }

    public void StartBreak()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot start break from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.BreakInProgress;
        breakStart = clock.Elapsed;
        reminderVisibleSince = null;
        ClearSuppressedReminderState();
        BreakStarted?.Invoke(this, EventArgs.Empty);
    }

    public void ManualStartBreak()
    {
        if (CurrentPhase is not (WorkCyclePhase.Working or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed
            or WorkCyclePhase.FocusMode))
            throw new InvalidOperationException(
                $"Cannot start break from phase {CurrentPhase}.");

        ExitFocusMode();

        CurrentPhase = WorkCyclePhase.BreakInProgress;
        breakStart = clock.Elapsed;
        pendingSince = null;
        reminderVisibleSince = null;
        snoozeUntil = null;
        wasPassivePaused = false;
        ClearSuppressedReminderState();
        BreakStarted?.Invoke(this, EventArgs.Empty);
    }

    public void CancelBreak()
    {
        if (CurrentPhase != WorkCyclePhase.BreakInProgress)
            return;

        lastTick = null;
        wasWorking = false;
        breakStart = null;

        if (!TryEnterPendingReminderFromWorking(clock.Elapsed))
            CurrentPhase = WorkCyclePhase.Working;

        BreakCancelled?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan WorkCycleThreshold => effectiveWorkInterval > TimeSpan.Zero
        ? effectiveWorkInterval
        : workInterval;

    public void Snooze()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot snooze from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.Snoozed;
        snoozeUntil = clock.Elapsed + snoozeDuration;
        reminderVisibleSince = null;
        ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.Snoozed));
    }

    public void Ignore()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot ignore from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.Working;
        pendingSince = null;
        breakStart = null;
        reminderVisibleSince = null;
        snoozeUntil = null;
        lastTick = null;
        wasWorking = false;
        var now = clock.Elapsed;
        cooldownUntil = now + retryCooldown;
        ArmNextDebtDeadline(now);
        CooldownStarted?.Invoke(this, EventArgs.Empty);
        ReminderDismissed?.Invoke(this, new ReminderDismissedEventArgs(ReminderResult.Ignored));
    }

    public void Pause(TimeSpan? pauseDuration = null)
    {
        if (CurrentPhase is not (WorkCyclePhase.Working or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed))
            throw new InvalidOperationException(
                $"Cannot pause from phase {CurrentPhase}.");

        if (pauseDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pauseDuration), "Pause duration cannot be negative.");

        CurrentPhase = WorkCyclePhase.Paused;
        pauseUntil = pauseDuration.HasValue ? clock.Elapsed + pauseDuration.Value : null;
        ClearReminderState();
        Paused?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (CurrentPhase != WorkCyclePhase.Paused)
            throw new InvalidOperationException(
                $"Cannot resume from phase {CurrentPhase}.");

        lastTick = null;
        wasWorking = false;
        pauseUntil = null;
        CurrentPhase = WorkCyclePhase.Working;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    public void StartFocusMode()
    {
        if (CurrentPhase is WorkCyclePhase.Paused or WorkCyclePhase.FocusMode or WorkCyclePhase.Disabled or WorkCyclePhase.BreakInProgress or WorkCyclePhase.Idle)
            throw new InvalidOperationException(
                $"Cannot start focus mode from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.FocusMode;
        focusModeUntil = clock.Elapsed + focusModeDuration;
        ClearReminderState();
        FocusModeStarted?.Invoke(this, EventArgs.Empty);
    }

    public void EndFocusMode()
    {
        if (CurrentPhase != WorkCyclePhase.FocusMode)
            throw new InvalidOperationException(
                $"Cannot end focus mode from phase {CurrentPhase}.");

        ExitFocusMode();

        if (!TryEnterPendingReminderFromWorking(clock.Elapsed))
            CurrentPhase = WorkCyclePhase.Working;
    }

    /// <summary>
    /// The single exit routine for Focus Mode: clears the focus deadline and raises
    /// <see cref="FocusModeEnded"/> exactly once. Every exit path — timer expiry,
    /// explicit end, idle entry, lock, sleep, disable, and manual break start — routes
    /// through here before performing its own phase transition. The phase guard is what
    /// makes it idempotent, so no path can double-count a focus session.
    /// </summary>
    private void ExitFocusMode()
    {
        if (CurrentPhase != WorkCyclePhase.FocusMode)
            return;

        focusModeUntil = null;
        FocusModeEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Disable()
    {
        if (CurrentPhase == WorkCyclePhase.Disabled)
            throw new InvalidOperationException("Already disabled.");

        ExitFocusMode();

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
            var now = clock.Elapsed;
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
            ExitFocusMode();
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
            ExitFocusMode();
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

    private void AccumulateIfWorking(TimeSpan now, bool isWorking)
    {
        if (isWorking && wasWorking && lastTick.HasValue)
        {
            var delta = now - lastTick.Value;
            AccumulatedWorkTime = RestNeedPolicy.Accumulate(AccumulatedWorkTime, delta);
        }

        wasWorking = isWorking;
        lastTick = now;
    }

    private void TickWorking(TimeSpan now, bool isWorking)
    {
        if (!isWorking)
        {
            EnterIdle();
            return;
        }

        TryEnterPendingReminderFromWorking(now);
    }

    private bool TryEnterPendingReminderFromWorking(TimeSpan now)
    {
        if (cooldownUntil.HasValue)
        {
            TimeSpan effective = EarlierOf(cooldownUntil, nextDebtDeadline)!.Value;
            if (now < effective)
                return false;

            bool wasDebtDeadline = nextDebtDeadline.HasValue && effective == nextDebtDeadline.Value;

            cooldownUntil = null;
            nextDebtDeadline = null;
            CooldownEnded?.Invoke(this, EventArgs.Empty);

            if (wasDebtDeadline)
            {
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSince = now;
                return true;
            }

            if (effectiveWorkInterval > TimeSpan.Zero &&
                AccumulatedWorkTime >= effectiveWorkInterval)
            {
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSince = now;
                return true;
            }

            return false;
        }

        if (effectiveWorkInterval > TimeSpan.Zero &&
            AccumulatedWorkTime >= effectiveWorkInterval)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSince = now;
            return true;
        }

        return false;
    }

    private void TickPending(TimeSpan now, bool isWorking, TimeSpan idleDuration)
    {
        if (ReminderTimingPolicy.IsIdle(idleDuration, idleThreshold))
        {
            EnterIdle();
            return;
        }

        if (wasPassivePaused && !ReminderTimingPolicy.IsPassivePause(idleDuration, passiveBreakThreshold))
        {
            pendingSince = now;
            wasPassivePaused = false;
        }

        if (ReminderTimingPolicy.IsPassivePause(idleDuration, passiveBreakThreshold))
        {
            if (!wasPassivePaused)
            {
                wasPassivePaused = true;
                PassivePauseDetected?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        var elapsed = pendingSince.HasValue ? now - pendingSince.Value : TimeSpan.Zero;

        var timingParams = new TimingParameters(
            IdleDuration: idleDuration,
            ElapsedInPhase: elapsed,
            NaturalPauseThreshold: naturalPauseThreshold,
            MaximumReminderWait: maximumReminderWait,
            PassiveBreakThreshold: passiveBreakThreshold,
            IdleThreshold: idleThreshold,
            ReminderDisplayDuration: reminderDisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var decision = ReminderTimingPolicy.EvaluatePendingReminder(timingParams);

        if (decision is TimingDecision.ShowReminder or TimingDecision.ShowReminderMaxWait)
        {
            EnterReminderVisible(now);
        }
    }

    private void TickReminderVisible(TimeSpan now, TimeSpan idleDuration)
    {
        var elapsed = reminderVisibleSince.HasValue ? now - reminderVisibleSince.Value : TimeSpan.Zero;

        var timingParams = new TimingParameters(
            IdleDuration: idleDuration,
            ElapsedInPhase: elapsed,
            NaturalPauseThreshold: naturalPauseThreshold,
            MaximumReminderWait: maximumReminderWait,
            PassiveBreakThreshold: passiveBreakThreshold,
            IdleThreshold: idleThreshold,
            ReminderDisplayDuration: reminderDisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var decision = ReminderTimingPolicy.EvaluateReminderVisible(timingParams);

        switch (decision)
        {
            case TimingDecision.EnterIdle:
                EnterIdle();
                break;

            case TimingDecision.PassivePauseDetected:
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSince = now;
                reminderVisibleSince = null;
                wasPassivePaused = true;
                PassivePauseDetected?.Invoke(this, EventArgs.Empty);
                break;

            case TimingDecision.AutoDismiss:
                TryAutoDismiss(now);
                break;
        }
    }

    private void TickSnoozed(TimeSpan now, bool isWorking, TimeSpan idleDuration)
    {
        if (ReminderTimingPolicy.IsIdle(idleDuration, idleThreshold))
        {
            EnterIdle();
            return;
        }

        if (now >= snoozeUntil!.Value)
        {
            CurrentPhase = WorkCyclePhase.PendingReminder;
            pendingSince = now;
            snoozeUntil = null;
        }
    }

    private void TickBreak(TimeSpan now)
    {
        if (now - breakStart!.Value >= breakDuration)
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
        pendingSince = null;
        breakStart = null;
        lastTick = null;
        wasWorking = false;
        reminderVisibleSince = null;
        snoozeUntil = null;
        wasPassivePaused = false;
        cooldownUntil = null;
        nextDebtDeadline = null;
        focusModeUntil = null;
        pauseUntil = null;
        restDebtLevel = RestDebtLevel.Level0;

        if (wasInBreak)
            BreakCancelled?.Invoke(this, EventArgs.Empty);
        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);
        IdleStarted?.Invoke(this, EventArgs.Empty);

        if (previousLevel != RestDebtLevel.Level0)
            RestDebtLevelChanged?.Invoke(this, new RestDebtLevelChangedEventArgs(previousLevel, RestDebtLevel.Level0));
    }

    private void EnterReminderVisible(TimeSpan now)
    {
        bool wasCooldownActive = cooldownUntil.HasValue;
        cooldownUntil = null;
        nextDebtDeadline = null;
        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);

        if (!forceAllowPopup)
        {
            var effective = GetEffectiveIntensity();

            if (effective < PresentationIntensity.LightTouch)
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

            if (effective < PresentationIntensity.EdgePopup)
            {
                showTrayCue = true;
                if (!hasSuppressedReminder)
                {
                    hasSuppressedReminder = true;
                    ReminderLightTouch?.Invoke(this, EventArgs.Empty);
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
        reminderVisibleSince = now;
        ReminderShown?.Invoke(this, EventArgs.Empty);
    }

    private void TryAutoDismiss(TimeSpan now)
    {
        var visibleElapsed = now - reminderVisibleSince!.Value;
        if (visibleElapsed >= reminderDisplayDuration)
        {
            CurrentPhase = WorkCyclePhase.Working;
            pendingSince = null;
            breakStart = null;
            lastTick = null;
            wasWorking = false;
            reminderVisibleSince = null;
            snoozeUntil = null;
            cooldownUntil = now + retryCooldown;
            ArmNextDebtDeadline(now);
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
        pendingSince = null;
        breakStart = null;
        lastTick = null;
        wasWorking = false;
        reminderVisibleSince = null;
        snoozeUntil = null;
        wasPassivePaused = false;
        isReminderSuppressed = false;
        hasSuppressedReminder = false;
        showTrayCue = false;
        cooldownUntil = null;
        nextDebtDeadline = null;
        focusModeUntil = null;
        pauseUntil = null;
        restDebtLevel = RestDebtLevel.Level0;

        if (wasCooldownActive)
            CooldownEnded?.Invoke(this, EventArgs.Empty);

        if (previousLevel != RestDebtLevel.Level0)
            RestDebtLevelChanged?.Invoke(this, new RestDebtLevelChangedEventArgs(previousLevel, RestDebtLevel.Level0));
    }

    private void EvaluateDebtLevel()
    {
        var newLevel = RestNeedPolicy.Evaluate(
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

    /// <summary>
    /// Safety net only. The threshold deadline is armed when the cooldown starts
    /// (see <see cref="ArmNextDebtDeadline"/>); recomputing it on a level change must
    /// never push a deadline that has already come due out into the future, or the
    /// retry gate below would skip the very crossing it was armed for.
    /// </summary>
    private void UpdateDebtDeadline()
    {
        if (!cooldownUntil.HasValue)
            return;

        var now = clock.Elapsed;
        if (nextDebtDeadline.HasValue && nextDebtDeadline.Value <= now)
            return;

        ArmNextDebtDeadline(now);
    }

    /// <summary>
    /// Arms the monotonic elapsed time at which the next rest-debt threshold will be reached,
    /// from the current accumulated work time. At the highest level there is no further
    /// threshold and the retry cooldown governs alone.
    /// </summary>
    /// <remarks>
    /// ADR-0003: the supplied deadline is stored only while a cooldown is active, so
    /// <see cref="cooldownUntil"/> must already be set when this runs. This ordering is
    /// a correctness requirement, not a style preference.
    /// </remarks>
    private void ArmNextDebtDeadline(TimeSpan now)
    {
        var nextThreshold = DebtPolicy.GetNextThreshold(
            restDebtLevel,
            workInterval,
            debtLevel2Threshold,
            debtLevel3Threshold,
            debtLevel4Threshold);

        if (nextThreshold.HasValue)
        {
            var remaining = nextThreshold.Value - AccumulatedWorkTime;
            SetNextDebtDeadline(remaining > TimeSpan.Zero ? now + remaining : now);
        }
        else
        {
            SetNextDebtDeadline(null);
        }
    }

    private void ClearReminderState()
    {
        pendingSince = null;
        breakStart = null;
        reminderVisibleSince = null;
        snoozeUntil = null;
        lastTick = null;
        wasWorking = false;
        wasPassivePaused = false;
        ClearSuppressedReminderState();
    }

    /// <summary>
    /// Forgets any held-back reminder attempt, so that a later context-cap lift has
    /// nothing stale left to promote.
    /// </summary>
    private void ClearSuppressedReminderState()
    {
        hasSuppressedReminder = false;
        showTrayCue = false;
    }

    private static TimeSpan? EarlierOf(TimeSpan? a, TimeSpan? b)
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

    private static void ValidateNonNegative(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName, value, "Threshold cannot be negative.");
    }
}
