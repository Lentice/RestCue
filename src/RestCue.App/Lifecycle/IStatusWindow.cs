using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Reminders;

namespace RestCue.App.Lifecycle;

public interface IStatusWindow
{
    event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;

    RestDebtLevel CurrentDebtLevel { get; }

    void ShowOrActivate();

    void StartBreakNow();

    void Pause();

    void PauseFor(TimeSpan duration);

    void Resume();

    void StartFocusMode();

    void EndFocusMode();

    void Disable();

    void Enable();

    void UpdateForegroundContextProvider(bool collectProcessNames);

    void UpdateApplicationRules(IEnumerable<ApplicationRule> rules);
}
