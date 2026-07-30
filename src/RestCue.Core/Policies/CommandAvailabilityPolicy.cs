using RestCue.Core.Reminders;

namespace RestCue.Core.Policies;

/// <summary>
/// Which reminder commands a surface may offer in a given work-cycle phase, and which
/// way each toggle currently points.
/// </summary>
/// <remarks>
/// Availability describes legality, not desirability. Disabling or pausing during a
/// running break is legal and cancels the break; the cancellation is an explicit,
/// recorded consequence rather than a reason to withhold the command.
/// </remarks>
public readonly record struct CommandAvailability
{
    public required bool CanPause { get; init; }

    public required bool CanResume { get; init; }

    public required bool CanStartFocusMode { get; init; }

    public required bool CanEndFocusMode { get; init; }

    public required bool CanDisable { get; init; }

    public required bool CanEnable { get; init; }

    public required bool CanBreakNow { get; init; }

    /// <summary>The pause control is showing "resume" rather than "pause".</summary>
    public required bool ShowResume { get; init; }

    /// <summary>The focus control is showing "end focus mode" rather than "focus mode".</summary>
    public required bool ShowEndFocusMode { get; init; }

    /// <summary>The disable control is showing "enable" rather than "disable".</summary>
    public required bool ShowEnable { get; init; }

    /// <summary>
    /// Whether the combined pause/resume control is actionable, given which way it points.
    /// </summary>
    public bool PauseToggleEnabled => ShowResume ? CanResume : CanPause;

    /// <summary>
    /// Whether the combined focus-mode control is actionable, given which way it points.
    /// </summary>
    public bool FocusToggleEnabled => ShowEndFocusMode ? CanEndFocusMode : CanStartFocusMode;

    /// <summary>
    /// Whether the combined disable/enable control is actionable, given which way it points.
    /// </summary>
    public bool DisableToggleEnabled => ShowEnable ? CanEnable : CanDisable;
}

/// <summary>
/// The one mapping from work-cycle phase to command availability. Every surface — the
/// tray menu, the main window's menu, the main window's buttons — reads it, so they
/// cannot disagree about what the user is allowed to do.
/// </summary>
/// <remarks>
/// Each rule below mirrors exactly what <see cref="WorkCycleTracker"/> accepts. The
/// pairing of this policy with its consistency test is what keeps a future surface from
/// drifting the way the tray and the main window did.
/// </remarks>
public static class CommandAvailabilityPolicy
{
    public static CommandAvailability ForPhase(WorkCyclePhase phase)
    {
        bool isActiveCycle = phase
            is WorkCyclePhase.Working
            or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible
            or WorkCyclePhase.Snoozed;

        return new CommandAvailability
        {
            CanPause = isActiveCycle,
            CanResume = phase == WorkCyclePhase.Paused,
            CanStartFocusMode = isActiveCycle,
            CanEndFocusMode = phase == WorkCyclePhase.FocusMode,
            CanDisable = phase != WorkCyclePhase.Disabled,
            CanEnable = phase == WorkCyclePhase.Disabled,
            CanBreakNow = isActiveCycle || phase == WorkCyclePhase.FocusMode,
            ShowResume = phase == WorkCyclePhase.Paused,
            ShowEndFocusMode = phase == WorkCyclePhase.FocusMode,
            ShowEnable = phase == WorkCyclePhase.Disabled,
        };
    }
}
