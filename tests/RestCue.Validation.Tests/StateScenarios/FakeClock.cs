using RestCue.Core.Time;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class FakeClock : IClock
{
    private DateTimeOffset utcNow = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private TimeSpan elapsed;

    public DateTimeOffset UtcNow => utcNow;

    public TimeSpan Elapsed => elapsed;

    public void Advance(TimeSpan duration)
    {
        utcNow += duration;
        elapsed += duration;
    }

    /// <summary>
    /// Moves civil time without moving elapsed time — a system clock step, forward or
    /// backward. Nothing that measures a duration may react to this.
    /// </summary>
    public void StepWallClock(TimeSpan delta) => utcNow += delta;

    /// <summary>
    /// Moves elapsed time without moving civil time, so that the two readings can be
    /// driven independently.
    /// </summary>
    public void AdvanceElapsedOnly(TimeSpan duration) => elapsed += duration;
}
