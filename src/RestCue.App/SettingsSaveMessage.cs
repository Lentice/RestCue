using RestCue.Core.Settings;

namespace RestCue.App;

/// <summary>
/// Builds the confirmation shown after settings are saved. It names the settings that
/// genuinely need a relaunch instead of vaguely warning that "some settings" do, so a
/// user who has just changed a privacy control knows it is already in force.
/// </summary>
public static class SettingsSaveMessage
{
    public const string AllActive = "設定已儲存並立即生效。";

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        [nameof(AppSettings.WorkInterval)] = "工作間隔",
        [nameof(AppSettings.NaturalPauseThreshold)] = "自然停頓門檻",
        [nameof(AppSettings.MaximumReminderWait)] = "最長提醒等待",
        [nameof(AppSettings.IdleThreshold)] = "離開判斷時間",
        [nameof(AppSettings.PassiveBreakThreshold)] = "被動休息門檻",
        [nameof(AppSettings.BreakDuration)] = "休息長度",
        [nameof(AppSettings.SnoozeDuration)] = "延後長度",
        [nameof(AppSettings.ReminderDisplayDuration)] = "提醒顯示時間",
        [nameof(AppSettings.RetryCooldown)] = "提醒重試冷卻",
        [nameof(AppSettings.DebtLevel2Threshold)] = "休息債務 Level 2",
        [nameof(AppSettings.DebtLevel3Threshold)] = "休息債務 Level 3",
        [nameof(AppSettings.DebtLevel4Threshold)] = "休息債務 Level 4",
        [nameof(AppSettings.FocusModeDuration)] = "專注模式長度",
    };

    public static string Build(IReadOnlyList<string> restartRequiringChanges)
    {
        ArgumentNullException.ThrowIfNull(restartRequiringChanges);

        if (restartRequiringChanges.Count == 0)
            return AllActive;

        var names = restartRequiringChanges.Select(DisplayNameFor);
        return $"設定已儲存。下列設定將於下次啟動時生效：{string.Join("、", names)}。其餘設定已立即生效。";
    }

    /// <summary>
    /// The user-facing label for a settings field, or the raw property name when the field
    /// has no entry. Also used when a validation error has no dedicated inline error box and
    /// has to be reported in the status area, so the user reads a label rather than a symbol.
    /// </summary>
    public static string DisplayNameFor(string field) =>
        DisplayNames.TryGetValue(field, out string? name) ? name : field;
}
