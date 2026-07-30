using RestCue.Core.Settings;
using RestCue.Core.Time;

namespace RestCue.Core.Reminders;

/// <summary>
/// The one place that maps stored settings onto the reminder engine's constructor.
/// Both the application's startup probe and the window that runs the engine go through
/// here, so a probe that succeeds cannot be followed by a real construction that fails.
/// </summary>
public static class WorkCycleTrackerFactory
{
    public static WorkCycleTracker Create(AppSettings settings, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        return new WorkCycleTracker(
            clock,
            settings.WorkInterval,
            settings.IdleThreshold,
            settings.NaturalPauseThreshold,
            settings.MaximumReminderWait,
            settings.BreakDuration,
            settings.PassiveBreakThreshold,
            settings.SnoozeDuration,
            settings.ReminderDisplayDuration,
            settings.RetryCooldown,
            settings.DebtLevel2Threshold,
            settings.DebtLevel3Threshold,
            settings.DebtLevel4Threshold,
            settings.FocusModeDuration);
    }
}
