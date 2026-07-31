using System.Diagnostics;
using RestCue.Core.Time;

namespace RestCue.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    /// <summary>
    /// Fixed at first use so that <see cref="Elapsed"/> readings taken from different
    /// <see cref="SystemClock"/> instances share one timeline.
    /// </summary>
    private static readonly long Origin = Stopwatch.GetTimestamp();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <remarks>
    /// Backed by the high-resolution performance counter, which counts forward from
    /// boot and is unaffected by system time changes. It does not advance while the
    /// machine is suspended, which is what we want: the sleep and resume handlers
    /// reset the cycle rather than crediting suspended time as work or rest.
    /// </remarks>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(Origin);
}
