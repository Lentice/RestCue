using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class ApplicationRuleSetTests
{
    [Fact]
    public void Find_returns_null_for_null_processName()
    {
        var set = new ApplicationRuleSet();
        Assert.Null(set.Find(null));
    }

    [Fact]
    public void Find_returns_null_for_empty_processName()
    {
        var set = new ApplicationRuleSet();
        Assert.Null(set.Find(""));
    }

    [Fact]
    public void Find_returns_null_for_unknown_process()
    {
        var rules = new[] { new ApplicationRule { ProcessName = "vlc", RuleType = ApplicationRuleType.TrayOnly } };
        var set = new ApplicationRuleSet(rules);
        Assert.Null(set.Find("notepad"));
    }

    [Fact]
    public void Find_returns_rule_for_matching_process_case_insensitive()
    {
        var rules = new[] { new ApplicationRule { ProcessName = "VLC", RuleType = ApplicationRuleType.Silent } };
        var set = new ApplicationRuleSet(rules);
        var rule = set.Find("vlc");
        Assert.NotNull(rule);
        Assert.Equal(ApplicationRuleType.Silent, rule.RuleType);
    }

    [Fact]
    public void IsSuppressingReminder_true_for_TrayOnly()
    {
        var rule = new ApplicationRule { ProcessName = "vlc", RuleType = ApplicationRuleType.TrayOnly };
        Assert.True(rule.IsSuppressingReminder);
    }

    [Fact]
    public void IsSuppressingReminder_true_for_Silent()
    {
        var rule = new ApplicationRule { ProcessName = "test", RuleType = ApplicationRuleType.Silent };
        Assert.True(rule.IsSuppressingReminder);
    }

    [Fact]
    public void IsSuppressingReminder_false_for_Normal()
    {
        var rule = new ApplicationRule { ProcessName = "test", RuleType = ApplicationRuleType.Normal };
        Assert.False(rule.IsSuppressingReminder);
    }

    [Fact]
    public void Empty_enumeration_creates_empty_set()
    {
        var set = new ApplicationRuleSet([]);
        Assert.Null(set.Find("anything"));
    }

    [Fact]
    public void Null_enumeration_creates_empty_set()
    {
        var set = new ApplicationRuleSet(null);
        Assert.Null(set.Find("anything"));
    }
}
