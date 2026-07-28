using RestCue.Core.Settings;
using Xunit;

namespace RestCue.Core.Tests.Settings;

public sealed class AppSettingsValidatorTests
{
    [Fact]
    public void Defaults_are_valid_and_disable_foreground_process_name_collection()
    {
        AppSettings settings = AppSettings.Default;
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Empty(errors);
        Assert.False(settings.CollectForegroundProcessNames);
    }

    [Fact]
    public void Passive_break_threshold_greater_than_idle_threshold_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            PassiveBreakThreshold = TimeSpan.FromSeconds(61),
            IdleThreshold = TimeSpan.FromMinutes(1),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        SettingsValidationError error = Assert.Single(errors);
        Assert.Equal("PassiveBreakThreshold", error.Field);
    }

    [Fact]
    public void Passive_break_threshold_equal_to_idle_threshold_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            PassiveBreakThreshold = TimeSpan.FromMinutes(1),
            IdleThreshold = TimeSpan.FromMinutes(1),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        SettingsValidationError error = Assert.Single(errors);
        Assert.Equal("PassiveBreakThreshold", error.Field);
    }
}
