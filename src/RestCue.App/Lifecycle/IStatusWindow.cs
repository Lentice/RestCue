using RestCue.Core.Domain;
using RestCue.Core.Events;

namespace RestCue.App.Lifecycle;

public interface IStatusWindow
{
    event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;

    RestDebtLevel CurrentDebtLevel { get; }

    void ShowOrActivate();

    void StartBreakNow();

    void Pause();

    void Resume();

    void StartFocusMode();

    void EndFocusMode();

    void Disable();

    void Enable();
}
