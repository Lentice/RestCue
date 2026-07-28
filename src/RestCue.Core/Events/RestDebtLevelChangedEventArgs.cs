using RestCue.Core.Domain;

namespace RestCue.Core.Events;

public sealed class RestDebtLevelChangedEventArgs : EventArgs
{
    public RestDebtLevel Previous { get; }
    public RestDebtLevel Current { get; }

    public RestDebtLevelChangedEventArgs(RestDebtLevel previous, RestDebtLevel current)
    {
        Previous = previous;
        Current = current;
    }
}
