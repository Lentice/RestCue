namespace RestCue.Core.Reminders;

public interface ISuggestionStore
{
    Task<IReadOnlySet<string>> GetDismissedProcessNamesAsync(CancellationToken cancellationToken = default);

    Task DismissAsync(string processName, CancellationToken cancellationToken = default);
}
