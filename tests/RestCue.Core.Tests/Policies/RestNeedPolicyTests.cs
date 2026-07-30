using RestCue.Core.Policies;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class RestNeedPolicyTests
{
    [Fact]
    public void Evaluate_returns_Level0_when_accumulated_time_below_work_interval()
    {
        var result = RestNeedPolicy.Evaluate(
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(60));
        Assert.Equal(Domain.RestDebtLevel.Level0, result);
    }

    [Fact]
    public void Evaluate_returns_Level1_when_accumulated_time_reaches_work_interval()
    {
        var result = RestNeedPolicy.Evaluate(
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(60));
        Assert.Equal(Domain.RestDebtLevel.Level1, result);
    }

    [Fact]
    public void Evaluate_returns_Level2_when_accumulated_time_reaches_level2_threshold()
    {
        var result = RestNeedPolicy.Evaluate(
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(60));
        Assert.Equal(Domain.RestDebtLevel.Level2, result);
    }

    [Fact]
    public void Evaluate_returns_Level3_when_accumulated_time_reaches_level3_threshold()
    {
        var result = RestNeedPolicy.Evaluate(
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(60));
        Assert.Equal(Domain.RestDebtLevel.Level3, result);
    }

    [Fact]
    public void Evaluate_returns_Level4_when_accumulated_time_reaches_level4_threshold()
    {
        var result = RestNeedPolicy.Evaluate(
            TimeSpan.FromMinutes(60),
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(60));
        Assert.Equal(Domain.RestDebtLevel.Level4, result);
    }

    [Fact]
    public void Accumulate_adds_delta_to_existing_time()
    {
        var result = RestNeedPolicy.Accumulate(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void Accumulate_ignores_zero_delta()
    {
        var result = RestNeedPolicy.Accumulate(TimeSpan.FromMinutes(10), TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(10), result);
    }

    [Fact]
    public void Accumulate_ignores_negative_delta()
    {
        var result = RestNeedPolicy.Accumulate(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(-5));
        Assert.Equal(TimeSpan.FromMinutes(10), result);
    }
}
