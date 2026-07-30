namespace RestCue.Core.Settings;

/// <summary>
/// Bounds from the settings contract that consumers outside the validator also need.
/// </summary>
/// <remarks>
/// The validator is the authority on legal values; this exists so a surface that has to
/// clamp — the reminder window applying opacity, for instance — does so against the
/// contract rather than against a number it restates locally.
/// </remarks>
public static class SettingsRanges
{
    /// <summary>20%, per the product contract's 提示透明度 range.</summary>
    public const double MinimumReminderOpacity = 0.2;

    public const double MaximumReminderOpacity = 1.0;
}
