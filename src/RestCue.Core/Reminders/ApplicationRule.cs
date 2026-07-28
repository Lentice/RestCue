namespace RestCue.Core.Reminders;

public sealed record ApplicationRule
{
    public string ProcessName { get; init; } = string.Empty;

    public ApplicationRuleType RuleType { get; init; } = ApplicationRuleType.Normal;

    public TimeSpan? CustomInterval { get; init; }

    public bool IsSuppressingReminder => RuleType is ApplicationRuleType.TrayOnly or ApplicationRuleType.Silent;
}
