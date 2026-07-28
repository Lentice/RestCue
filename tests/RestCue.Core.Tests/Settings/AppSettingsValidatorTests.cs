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

    [Fact]
    public void RetryCooldown_below_minimum_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(0),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "RetryCooldown");
    }

    [Fact]
    public void RetryCooldown_above_maximum_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(61),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "RetryCooldown");
    }

    [Fact]
    public void RetryCooldown_at_minimum_is_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(1),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, e => e.Field == "RetryCooldown");
    }

    [Fact]
    public void RetryCooldown_at_maximum_is_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(60),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, e => e.Field == "RetryCooldown");
    }
}
