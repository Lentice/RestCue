using RestCue.Core.Activity;
using Xunit;

namespace RestCue.Core.Tests.Activity;

public sealed class UserActivityStatusEvaluatorTests
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);

    [Fact]
    public void Evaluate_ReturnsWorking_ImmediatelyBeforeIdleThreshold()
    {
        var evaluator = new UserActivityStatusEvaluator(IdleThreshold);

        UserActivityStatus status = evaluator.Evaluate(
            UserActivitySample.Available(IdleThreshold - TimeSpan.FromMilliseconds(1)));

        Assert.Equal(UserActivityStatus.Working, status);
    }

    [Fact]
    public void Evaluate_ReturnsIdle_AtIdleThreshold()
    {
        var evaluator = new UserActivityStatusEvaluator(IdleThreshold);

        UserActivityStatus status = evaluator.Evaluate(
            UserActivitySample.Available(IdleThreshold));

        Assert.Equal(UserActivityStatus.Idle, status);
    }

    [Fact]
    public void Evaluate_ReturnsWorking_WhenInputResumes()
    {
        var evaluator = new UserActivityStatusEvaluator(IdleThreshold);
        Assert.Equal(
            UserActivityStatus.Idle,
            evaluator.Evaluate(UserActivitySample.Available(IdleThreshold)));

        UserActivityStatus status = evaluator.Evaluate(
            UserActivitySample.Available(TimeSpan.Zero));

        Assert.Equal(UserActivityStatus.Working, status);
    }

    [Fact]
    public void Evaluate_FailsSafeToIdle_WhenActivityIsUnavailable()
    {
        var evaluator = new UserActivityStatusEvaluator(IdleThreshold);

        UserActivityStatus status = evaluator.Evaluate(UserActivitySample.Unavailable);

        Assert.Equal(UserActivityStatus.Idle, status);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UserActivityStatusEvaluator(TimeSpan.Zero));
    }
}
