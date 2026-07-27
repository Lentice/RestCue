using RestCue.Core.Activity;
using Xunit;

namespace RestCue.Core.Tests.Activity;

public sealed class UserActivityStatusTrackerTests
{
    [Fact]
    public void Refresh_UsesFakeActivitySource_ForThresholdAndRecovery()
    {
        var source = new FakeActivityMonitor();
        var tracker = new UserActivityStatusTracker(
            source,
            new UserActivityStatusEvaluator(TimeSpan.FromSeconds(10)));

        source.Sample = UserActivitySample.Available(TimeSpan.FromSeconds(9));
        Assert.Equal(UserActivityStatus.Working, tracker.Refresh());

        source.Sample = UserActivitySample.Available(TimeSpan.FromSeconds(10));
        Assert.Equal(UserActivityStatus.Idle, tracker.Refresh());

        source.Sample = UserActivitySample.Available(TimeSpan.Zero);
        Assert.Equal(UserActivityStatus.Working, tracker.Refresh());
        Assert.Equal(3, source.ReadCount);
    }

    private sealed class FakeActivityMonitor : IUserActivityMonitor
    {
        public UserActivitySample Sample { get; set; } = UserActivitySample.Unavailable;

        public int ReadCount { get; private set; }

        public UserActivitySample GetCurrentActivity()
        {
            ReadCount++;
            return Sample;
        }
    }
}
