namespace RestCue.Core.Activity;

public readonly record struct UserActivitySample
{
    private UserActivitySample(bool isAvailable, TimeSpan idleDuration)
    {
        IsAvailable = isAvailable;
        IdleDuration = idleDuration;
    }

    public static UserActivitySample Unavailable { get; } = new(false, TimeSpan.Zero);

    public bool IsAvailable { get; }

    public TimeSpan IdleDuration { get; }

    public static UserActivitySample Available(TimeSpan idleDuration)
    {
        if (idleDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleDuration),
                idleDuration,
                "Idle duration cannot be negative.");
        }

        return new UserActivitySample(true, idleDuration);
    }
}
