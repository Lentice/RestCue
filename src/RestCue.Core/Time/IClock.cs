namespace RestCue.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

