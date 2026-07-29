namespace RestCue.Core.UsageEvents;

public enum UsageEventType
{
    ReminderShown,
    BreakStarted,
    BreakCompleted,
    BreakCancelled,
    PassivePauseDetected,
    ReminderDismissed,
    IdleStarted,
    IdleEnded,
    CooldownStarted,
    CooldownEnded,
    Paused,
    Resumed,
    FocusModeStarted,
    FocusModeEnded,
    Disabled,
    Enabled,
    ForegroundProcessChanged,
    RestDebtLevelChanged
}
