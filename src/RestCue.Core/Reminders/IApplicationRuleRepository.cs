namespace RestCue.Core.Reminders;

public interface IApplicationRuleRepository
{
    Task<IReadOnlyList<ApplicationRule>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ApplicationRule rule, CancellationToken cancellationToken = default);

    Task DeleteAsync(string processName, CancellationToken cancellationToken = default);
}
