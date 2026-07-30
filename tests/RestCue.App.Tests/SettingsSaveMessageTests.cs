using RestCue.App;
using RestCue.Core.Settings;
using Xunit;

namespace RestCue.App.Tests;

public sealed class SettingsSaveMessageTests
{
    [Fact]
    public void No_restart_requiring_change_reports_settings_active()
    {
        Assert.Equal(SettingsSaveMessage.AllActive, SettingsSaveMessage.Build([]));
    }

    [Fact]
    public void A_single_change_is_named()
    {
        string message = SettingsSaveMessage.Build([nameof(AppSettings.IdleThreshold)]);

        Assert.Contains("離開判斷時間", message);
        Assert.Contains("下次啟動", message);
    }

    [Fact]
    public void Several_changes_are_all_named()
    {
        string message = SettingsSaveMessage.Build(
            [nameof(AppSettings.WorkInterval), nameof(AppSettings.RetryCooldown)]);

        Assert.Contains("工作間隔", message);
        Assert.Contains("提醒重試冷卻", message);
    }

    [Fact]
    public void Every_restart_requiring_field_has_a_display_name()
    {
        string message = SettingsSaveMessage.Build(RestartRequiredSettings.All);

        // A missing entry would fall through to the raw property name.
        foreach (string field in RestartRequiredSettings.All)
        {
            Assert.DoesNotContain(field, message);
        }
    }

    [Fact]
    public void A_named_change_still_confirms_that_the_rest_is_active()
    {
        string message = SettingsSaveMessage.Build([nameof(AppSettings.WorkInterval)]);

        Assert.Contains("已立即生效", message);
    }
}
