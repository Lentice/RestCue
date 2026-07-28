using RestCue.Core.Domain;
using RestCue.Core.Policies;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class DebtPolicyTests
{
    private static readonly TimeSpan L1 = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan L2 = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan L3 = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan L4 = TimeSpan.FromMinutes(60);

    [Fact]
    public void Evaluate_returns_Level0_when_below_level1()
    {
        var level = DebtPolicy.Evaluate(TimeSpan.FromMinutes(19), L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level0, level);
    }

    [Fact]
    public void Evaluate_returns_Level0_at_zero()
    {
        var level = DebtPolicy.Evaluate(TimeSpan.Zero, L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level0, level);
    }

    [Fact]
    public void Evaluate_returns_Level1_at_exact_level1()
    {
        var level = DebtPolicy.Evaluate(L1, L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level1, level);
    }

    [Fact]
    public void Evaluate_returns_Level1_just_above_level1()
    {
        var level = DebtPolicy.Evaluate(L1 + TimeSpan.FromMinutes(1), L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level1, level);
    }

    [Fact]
    public void Evaluate_returns_Level2_at_exact_level2()
    {
        var level = DebtPolicy.Evaluate(L2, L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level2, level);
    }

    [Fact]
    public void Evaluate_returns_Level2_just_below_level2()
    {
        var level = DebtPolicy.Evaluate(L2 - TimeSpan.FromMilliseconds(1), L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level1, level);
    }

    [Fact]
    public void Evaluate_returns_Level3_at_exact_level3()
    {
        var level = DebtPolicy.Evaluate(L3, L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level3, level);
    }

    [Fact]
    public void Evaluate_returns_Level4_at_exact_level4()
    {
        var level = DebtPolicy.Evaluate(L4, L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level4, level);
    }

    [Fact]
    public void Evaluate_returns_Level4_above_level4()
    {
        var level = DebtPolicy.Evaluate(L4 + TimeSpan.FromHours(1), L1, L2, L3, L4);
        Assert.Equal(RestDebtLevel.Level4, level);
    }

    [Fact]
    public void GetNextThreshold_returns_level1_for_Level0()
    {
        var t = DebtPolicy.GetNextThreshold(RestDebtLevel.Level0, L1, L2, L3, L4);
        Assert.Equal(L1, t);
    }

    [Fact]
    public void GetNextThreshold_returns_level2_for_Level1()
    {
        var t = DebtPolicy.GetNextThreshold(RestDebtLevel.Level1, L1, L2, L3, L4);
        Assert.Equal(L2, t);
    }

    [Fact]
    public void GetNextThreshold_returns_level3_for_Level2()
    {
        var t = DebtPolicy.GetNextThreshold(RestDebtLevel.Level2, L1, L2, L3, L4);
        Assert.Equal(L3, t);
    }

    [Fact]
    public void GetNextThreshold_returns_level4_for_Level3()
    {
        var t = DebtPolicy.GetNextThreshold(RestDebtLevel.Level3, L1, L2, L3, L4);
        Assert.Equal(L4, t);
    }

    [Fact]
    public void GetNextThreshold_returns_null_for_Level4()
    {
        var t = DebtPolicy.GetNextThreshold(RestDebtLevel.Level4, L1, L2, L3, L4);
        Assert.Null(t);
    }

    [Fact]
    public void ValidateThresholds_accepts_strictly_increasing()
    {
        DebtPolicy.ValidateThresholds(L1, L2, L3, L4);
    }

    [Fact]
    public void ValidateThresholds_throws_when_level1_not_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebtPolicy.ValidateThresholds(TimeSpan.Zero, L2, L3, L4));
    }

    [Fact]
    public void ValidateThresholds_throws_when_level2_not_greater_than_level1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebtPolicy.ValidateThresholds(L1, L1, L3, L4));
    }

    [Fact]
    public void ValidateThresholds_throws_when_level3_not_greater_than_level2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebtPolicy.ValidateThresholds(L1, L2, L2, L4));
    }

    [Fact]
    public void ValidateThresholds_throws_when_level4_not_greater_than_level3()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebtPolicy.ValidateThresholds(L1, L2, L3, L3));
    }
}
