namespace RestCue.Core.Reminders;

public enum WorkCyclePhase
{
    Working,
    PendingReminder,
    ReminderVisible,
    BreakInProgress,
    Snoozed,
    Idle,
    Paused,
    FocusMode,
    Disabled
}
