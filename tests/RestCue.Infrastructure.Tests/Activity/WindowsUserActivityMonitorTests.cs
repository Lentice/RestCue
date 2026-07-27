using RestCue.Infrastructure.Activity;
using Xunit;

namespace RestCue.Infrastructure.Tests.Activity;

public sealed class WindowsUserActivityMonitorTests
{
    [Fact]
    public void GetCurrentActivity_ReturnsElapsedTimeSinceLastInput()
    {
        var api = new FakeLastInputApi
        {
            CurrentTickCount = 1_500,
            LastInputTickCount = 1_000
        };
        var monitor = new WindowsUserActivityMonitor(api);

        var sample = monitor.GetCurrentActivity();

        Assert.True(sample.IsAvailable);
        Assert.Equal(TimeSpan.FromMilliseconds(500), sample.IdleDuration);
    }

    [Fact]
    public void GetCurrentActivity_HandlesTickCountWraparound()
    {
        var api = new FakeLastInputApi
        {
            CurrentTickCount = 10,
            LastInputTickCount = uint.MaxValue - 9
        };
        var monitor = new WindowsUserActivityMonitor(api);

        var sample = monitor.GetCurrentActivity();

        Assert.True(sample.IsAvailable);
        Assert.Equal(TimeSpan.FromMilliseconds(20), sample.IdleDuration);
    }

    [Fact]
    public void GetCurrentActivity_ReturnsUnavailable_WhenWindowsApiFails()
    {
        var monitor = new WindowsUserActivityMonitor(
            new FakeLastInputApi { IsSuccessful = false });

        var sample = monitor.GetCurrentActivity();

        Assert.False(sample.IsAvailable);
        Assert.Equal(TimeSpan.Zero, sample.IdleDuration);
    }

    private sealed class FakeLastInputApi : ILastInputApi
    {
        public bool IsSuccessful { get; init; } = true;

        public uint CurrentTickCount { get; init; }

        public uint LastInputTickCount { get; init; }

        public bool TryGetLastInputTickCount(out uint tickCount)
        {
            tickCount = LastInputTickCount;
            return IsSuccessful;
        }

        public uint GetTickCount() => CurrentTickCount;
    }
}
