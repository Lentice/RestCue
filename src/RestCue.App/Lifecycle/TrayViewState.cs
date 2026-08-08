using RestCue.Core.Domain;
using RestCue.Core.Reminders;

namespace RestCue.App.Lifecycle;

/// <summary>
/// The tray's complete presentation state: which mode the reminder engine is in, the
/// current rest-debt level, and whether reminders are currently suppressed. The tooltip
/// and the icon are both derived from all three, so no combination can be dropped the
/// way the independent setters used to.
/// </summary>
public readonly record struct TrayViewState(
    WorkCyclePhase Mode,
    RestDebtLevel DebtLevel,
    bool IsSuppressed);
