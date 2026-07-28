using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class DefaultApplicationRulesTests
{
    [Fact]
    public void All_returns_expected_rules()
    {
        var rules = DefaultApplicationRules.All.ToList();

        Assert.Contains(rules, r => r.ProcessName == "vlc" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "mpc-hc" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "mpc-hc64" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "mpc-be" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "potplayer" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "wmplayer" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "POWERPNT" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "zoom" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(rules, r => r.ProcessName == "CiscoCollabHost" && r.RuleType == ApplicationRuleType.TrayOnly);
    }

    [Fact]
    public void All_rules_are_TrayOnly()
    {
        var rules = DefaultApplicationRules.All.ToList();
        Assert.All(rules, r => Assert.Equal(ApplicationRuleType.TrayOnly, r.RuleType));
    }

    [Fact]
    public void All_rules_have_non_empty_ProcessName()
    {
        var rules = DefaultApplicationRules.All.ToList();
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.ProcessName)));
    }

    [Fact]
    public void All_rules_have_null_CustomInterval()
    {
        var rules = DefaultApplicationRules.All.ToList();
        Assert.All(rules, r => Assert.Null(r.CustomInterval));
    }
}
