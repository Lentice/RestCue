using RestCue.Core.Settings;
using Xunit;

namespace RestCue.Core.Tests.Settings;

public sealed class RestartRequiredSettingsTests
{
    [Fact]
    public void Identical_settings_require_no_restart()
    {
        Assert.Empty(RestartRequiredSettings.Changed(AppSettings.Default, AppSettings.Default));
    }

    [Theory]
    [InlineData(nameof(AppSettings.ReminderOpacity))]
    [InlineData(nameof(AppSettings.CollectForegroundProcessNames))]
    [InlineData(nameof(AppSettings.ReduceMotion))]
    [InlineData(nameof(AppSettings.BreakGuideMode))]
    [InlineData(nameof(AppSettings.LightTouchSoundEnabled))]
    [InlineData(nameof(AppSettings.DebtLevelTrayNotificationEnabled))]
    [InlineData(nameof(AppSettings.SnoozeDuration))]
    public void Live_appliable_settings_are_not_restart_requiring(string field)
    {
        Assert.DoesNotContain(field, RestartRequiredSettings.All);
    }

    [Fact]
    public void Changing_only_live_appliable_settings_requires_no_restart()
    {
        AppSettings next = AppSettings.Default with
        {
            ReminderOpacity = 0.4,
            CollectForegroundProcessNames = true,
            ReduceMotion = true,
            BreakGuideMode = BreakGuideMode.Voice,
            LightTouchSoundEnabled = false,
            DebtLevelTrayNotificationEnabled = false,
            SnoozeDuration = TimeSpan.FromMinutes(10),
        };

        Assert.Empty(RestartRequiredSettings.Changed(AppSettings.Default, next));
    }

    [Fact]
    public void A_changed_engine_parameter_is_reported()
    {
        AppSettings next = AppSettings.Default with { IdleThreshold = TimeSpan.FromMinutes(5) };

        string field = Assert.Single(RestartRequiredSettings.Changed(AppSettings.Default, next));
        Assert.Equal(nameof(AppSettings.IdleThreshold), field);
    }

    [Fact]
    public void Several_changed_engine_parameters_are_reported_in_dialog_order()
    {
        AppSettings next = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(30),
            WorkInterval = TimeSpan.FromMinutes(25),
        };

        Assert.Equal(
            [nameof(AppSettings.WorkInterval), nameof(AppSettings.RetryCooldown)],
            RestartRequiredSettings.Changed(AppSettings.Default, next));
    }

    [Fact]
    public void Snooze_duration_applies_without_a_restart()
    {
        // It holds no accumulated state, so the engine takes it in place.
        AppSettings next = AppSettings.Default with { SnoozeDuration = TimeSpan.FromMinutes(10) };

        Assert.Empty(RestartRequiredSettings.Changed(AppSettings.Default, next));
        Assert.DoesNotContain(nameof(AppSettings.SnoozeDuration), RestartRequiredSettings.All);
    }
}
