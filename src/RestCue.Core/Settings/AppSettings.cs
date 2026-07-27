namespace RestCue.Core.Settings;

public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    public TimeSpan WorkInterval { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan NaturalPauseThreshold { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumReminderWait { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan SnoozeDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PassiveBreakThreshold { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan ReminderDisplayDuration { get; init; } = TimeSpan.FromSeconds(30);

    public double ReminderOpacity { get; init; } = 0.7;

    public bool CollectForegroundProcessNames { get; init; }
}
