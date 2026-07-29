using RestCue.Core.Time;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class FakeClock : IClock
{
    private DateTimeOffset _utcNow = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow += duration;
}
