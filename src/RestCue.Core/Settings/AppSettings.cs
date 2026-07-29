namespace RestCue.Core.Settings;

public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    public int SchemaVersion { get; init; } = 2;

    public TimeSpan WorkInterval { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan NaturalPauseThreshold { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumReminderWait { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan SnoozeDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan RetryCooldown { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PassiveBreakThreshold { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan ReminderDisplayDuration { get; init; } = TimeSpan.FromSeconds(30);

    public double ReminderOpacity { get; init; } = 0.7;

    public bool CollectForegroundProcessNames { get; init; }

    public TimeSpan DebtLevel2Threshold { get; init; } = TimeSpan.FromMinutes(35);

    public TimeSpan DebtLevel3Threshold { get; init; } = TimeSpan.FromMinutes(45);

    public TimeSpan DebtLevel4Threshold { get; init; } = TimeSpan.FromMinutes(60);

    public BreakGuideMode BreakGuideMode { get; init; } = BreakGuideMode.Cue;

}
