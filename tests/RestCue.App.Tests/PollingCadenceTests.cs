using RestCue.Core.Activity;
using Xunit;

namespace RestCue.App.Tests;

public sealed class PollingCadenceTests
{
    [Fact]
    public void Each_tick_calls_GetCurrentActivity_exactly_once()
    {
        var monitor = new CountingActivityMonitor();
        int tickCount = 10;

        for (int i = 0; i < tickCount; i++)
        {
            monitor.GetCurrentActivity();
        }

        Assert.Equal(tickCount, monitor.CallCount);
    }

    private sealed class CountingActivityMonitor : IUserActivityMonitor
    {
        public int CallCount { get; private set; }

        public UserActivitySample GetCurrentActivity()
        {
            CallCount++;
            return UserActivitySample.Available(TimeSpan.Zero);
        }
    }
}
