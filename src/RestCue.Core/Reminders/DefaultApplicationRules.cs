namespace RestCue.Core.Reminders;

public static class DefaultApplicationRules
{
    private static readonly ApplicationRule[] Rules =
    [
        new() { ProcessName = "vlc", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "mpc-hc", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "mpc-hc64", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "mpc-be", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "potplayer", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "wmplayer", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "POWERPNT", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "zoom", RuleType = ApplicationRuleType.TrayOnly },
        new() { ProcessName = "CiscoCollabHost", RuleType = ApplicationRuleType.TrayOnly },
    ];

    public static IEnumerable<ApplicationRule> All => Rules;
}
