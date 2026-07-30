namespace RestCue.Core.Policies;

public enum TimingDecision
{
    None,
    ShowReminder,
    ShowReminderMaxWait,
    PassivePauseDetected,
    EnterIdle,
    AutoDismiss,
}

public readonly record struct TimingParameters(
    TimeSpan IdleDuration,
    TimeSpan ElapsedInPhase,
    TimeSpan NaturalPauseThreshold,
    TimeSpan MaximumReminderWait,
    TimeSpan PassiveBreakThreshold,
    TimeSpan IdleThreshold,
    TimeSpan ReminderDisplayDuration,
    bool IsPaused,
    bool IsFocused,
    bool IsFullscreen,
    bool IsMuted);

public static class ReminderTimingPolicy
{
    public static TimingDecision EvaluatePendingReminder(in TimingParameters p)
    {
        if (p.IdleDuration >= p.IdleThreshold)
            return TimingDecision.EnterIdle;

        if (p.IdleDuration >= p.PassiveBreakThreshold)
            return TimingDecision.PassivePauseDetected;

        if (p.IdleDuration >= p.NaturalPauseThreshold)
            return TimingDecision.ShowReminder;

        if (p.ElapsedInPhase >= p.MaximumReminderWait)
            return TimingDecision.ShowReminderMaxWait;

        return TimingDecision.None;
    }

    public static TimingDecision EvaluateReminderVisible(in TimingParameters p)
    {
        if (p.IdleDuration >= p.IdleThreshold)
            return TimingDecision.EnterIdle;

        if (p.IdleDuration >= p.PassiveBreakThreshold)
            return TimingDecision.PassivePauseDetected;

        if (p.ElapsedInPhase >= p.ReminderDisplayDuration)
            return TimingDecision.AutoDismiss;

        return TimingDecision.None;
    }

    public static TimingDecision EvaluateSnoozed(in TimingParameters p)
    {
        if (p.IdleDuration >= p.IdleThreshold)
            return TimingDecision.EnterIdle;

        if (p.ElapsedInPhase >= TimeSpan.Zero && p.ElapsedInPhase >= p.MaximumReminderWait * 10)
            return TimingDecision.ShowReminder;

        return TimingDecision.None;
    }

    public static bool IsNaturalPause(TimeSpan idleDuration, TimeSpan naturalPauseThreshold)
    {
        return idleDuration >= naturalPauseThreshold;
    }

    public static bool IsIdle(TimeSpan idleDuration, TimeSpan idleThreshold)
    {
        return idleDuration >= idleThreshold;
    }

    public static bool IsPassivePause(TimeSpan idleDuration, TimeSpan passiveBreakThreshold)
    {
        return idleDuration >= passiveBreakThreshold;
    }

    public static bool ShouldShowReminder(
        bool isWorking,
        bool isCooldownActive,
        bool hasPendingDebt,
        TimeSpan? cooldownUntil,
        DateTimeOffset? nextDebtDeadline,
        DateTimeOffset now)
    {
        if (isCooldownActive)
            return false;

        if (hasPendingDebt && nextDebtDeadline.HasValue && now >= nextDebtDeadline.Value)
            return true;

        return isWorking;
    }
}
