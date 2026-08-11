namespace RestCue.Core.Settings;

/// <summary>
/// How long a light-touch notification stays on screen.
/// </summary>
public enum NotificationDuration
{
    /// <summary>The default duration, roughly 7 seconds.</summary>
    Default = 0,

    /// <summary>The long duration, roughly 25 seconds.</summary>
    Long = 1,

    /// <summary>Stays until the next notification or application shutdown.</summary>
    UntilDismissed = 2,
}
