using RestCue.Core.Activity;

namespace RestCue.Infrastructure.Activity;

public sealed class WindowsUserActivityMonitor : IUserActivityMonitor
{
    private readonly ILastInputApi lastInputApi;

    public WindowsUserActivityMonitor()
        : this(new WindowsLastInputApi())
    {
    }

    internal WindowsUserActivityMonitor(ILastInputApi lastInputApi)
    {
        this.lastInputApi = lastInputApi;
    }

    public UserActivitySample GetCurrentActivity()
    {
        if (!lastInputApi.TryGetLastInputTickCount(out uint lastInputTickCount))
        {
            return UserActivitySample.Unavailable;
        }

        uint elapsedMilliseconds = unchecked(
            lastInputApi.GetTickCount() - lastInputTickCount);
        return UserActivitySample.Available(
            TimeSpan.FromMilliseconds(elapsedMilliseconds));
    }
}
