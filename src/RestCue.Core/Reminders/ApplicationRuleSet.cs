namespace RestCue.Core.Reminders;

public sealed class ApplicationRuleSet
{
    private readonly IReadOnlyDictionary<string, ApplicationRule> rules;

    public ApplicationRuleSet(IEnumerable<ApplicationRule>? rules = null)
    {
        this.rules = (rules ?? [])
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ProcessName))
            .ToDictionary(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase);
    }

    public ApplicationRule? Find(string? processName) =>
        processName is not null && rules.TryGetValue(processName, out var rule) ? rule : null;
}
