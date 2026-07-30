using RestCue.Core.Reminders;

namespace RestCue.Core.Policies;

/// <summary>
/// Whether a work-cycle phase counts as continuous work.
/// </summary>
/// <remarks>
/// The product contract is explicit that the reminder lifecycle does not interrupt
/// continuous work: snooze, ignore, and auto-dismiss must not break the stretch, and
/// Focus Mode keeps accumulating. Only four things genuinely interrupt it — a break in
/// progress, going idle, pausing, and disabling reminders.
/// <para>
/// Deriving work sessions from equality against a single phase was what chopped a long
/// stretch of ignored reminders into fragments in the daily statistics.
/// </para>
/// </remarks>
public static class ContinuousWorkPolicy
{
    public static bool IsContinuousWork(WorkCyclePhase phase) => phase switch
    {
        WorkCyclePhase.Working => true,
        WorkCyclePhase.PendingReminder => true,
        WorkCyclePhase.ReminderVisible => true,
        WorkCyclePhase.Snoozed => true,
        WorkCyclePhase.FocusMode => true,
        WorkCyclePhase.BreakInProgress => false,
        WorkCyclePhase.Idle => false,
        WorkCyclePhase.Paused => false,
        WorkCyclePhase.Disabled => false,
        _ => false,
    };
}
