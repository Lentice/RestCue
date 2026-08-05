namespace RestCue.Core.Settings;

/// <summary>
/// How long a tray notification stays on screen. Windows only exposes these three
/// buckets to toast senders, not an arbitrary number of seconds.
/// </summary>
public enum NotificationDuration
{
    /// <summary>The system default, roughly 7 seconds.</summary>
    Default = 0,

    /// <summary>The long bucket, roughly 25 seconds.</summary>
    Long = 1,

    /// <summary>Stays until the user dismisses it (toast "reminder" scenario).</summary>
    UntilDismissed = 2,
}
