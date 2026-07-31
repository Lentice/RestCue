using RestCue.Core.Time;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class FakeClock : IClock
{
    private DateTimeOffset utcNow = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => utcNow;

    public void Advance(TimeSpan duration) => utcNow += duration;
}
