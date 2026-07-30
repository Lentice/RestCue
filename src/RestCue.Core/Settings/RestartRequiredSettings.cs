namespace RestCue.Core.Settings;

/// <summary>
/// The settings that are constructor parameters of the reminder engine, and therefore
/// only take effect on the next launch.
/// </summary>
/// <remarks>
/// Rebuilding the engine when one of these changes would discard the accumulated work
/// time — a trusted reset the user did not ask for — so the trade-off is to keep them
/// until relaunch and to say so. Everything not listed here applies on save.
/// <para>
/// Snooze duration is deliberately absent: it holds no accumulated state, so the engine
/// accepts it in place via <c>UpdateSnoozeDuration</c> and it applies to the next snooze.
/// </para>
/// <para>
/// Focus-mode duration is here because it is an engine parameter, not because any
/// current dialog control changes it.
/// </para>
/// </remarks>
public static class RestartRequiredSettings
{
    private static readonly (string Field, Func<AppSettings, object> Value)[] Fields =
    [
        (nameof(AppSettings.WorkInterval), s => s.WorkInterval),
        (nameof(AppSettings.IdleThreshold), s => s.IdleThreshold),
        (nameof(AppSettings.NaturalPauseThreshold), s => s.NaturalPauseThreshold),
        (nameof(AppSettings.MaximumReminderWait), s => s.MaximumReminderWait),
        (nameof(AppSettings.BreakDuration), s => s.BreakDuration),
        (nameof(AppSettings.PassiveBreakThreshold), s => s.PassiveBreakThreshold),
        (nameof(AppSettings.ReminderDisplayDuration), s => s.ReminderDisplayDuration),
        (nameof(AppSettings.RetryCooldown), s => s.RetryCooldown),
        (nameof(AppSettings.DebtLevel2Threshold), s => s.DebtLevel2Threshold),
        (nameof(AppSettings.DebtLevel3Threshold), s => s.DebtLevel3Threshold),
        (nameof(AppSettings.DebtLevel4Threshold), s => s.DebtLevel4Threshold),
        (nameof(AppSettings.FocusModeDuration), s => s.FocusModeDuration),
    ];

    /// <summary>All restart-requiring field names, in dialog order.</summary>
    public static IReadOnlyList<string> All { get; } = Fields.Select(f => f.Field).ToArray();

    /// <summary>
    /// The restart-requiring fields that differ between two settings snapshots, in dialog
    /// order. Empty when everything the user changed applies immediately.
    /// </summary>
    public static IReadOnlyList<string> Changed(AppSettings previous, AppSettings next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        return Fields
            .Where(f => !Equals(f.Value(previous), f.Value(next)))
            .Select(f => f.Field)
            .ToArray();
    }
}
