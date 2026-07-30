namespace RestCue.App;

internal static class PausePresets
{
    public static readonly PausePreset FifteenMinutes =
        new("15 分鐘", TimeSpan.FromMinutes(15));

    public static readonly PausePreset ThirtyMinutes =
        new("30 分鐘", TimeSpan.FromMinutes(30));

    public static readonly PausePreset OneHour =
        new("1 小時", TimeSpan.FromHours(1));
}

internal readonly record struct PausePreset(string Label, TimeSpan Duration);
