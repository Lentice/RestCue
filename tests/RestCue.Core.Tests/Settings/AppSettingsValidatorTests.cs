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
    public void Natural_pause_threshold_below_passive_break_threshold_is_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            NaturalPauseThreshold = TimeSpan.FromSeconds(29),
            PassiveBreakThreshold = TimeSpan.FromSeconds(30),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Empty(errors);
    }

    [Fact]
    public void Natural_pause_threshold_equal_to_passive_break_threshold_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            NaturalPauseThreshold = TimeSpan.FromSeconds(30),
            PassiveBreakThreshold = TimeSpan.FromSeconds(30),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        SettingsValidationError error = Assert.Single(errors);
        Assert.Equal("NaturalPauseThreshold", error.Field);
    }

    [Fact]
    public void Natural_pause_threshold_above_passive_break_threshold_is_invalid()
    {
        // The combination the product used to accept: passive pause always wins the
        // evaluation, so natural-pause reminders silently stop existing.
        AppSettings settings = AppSettings.Default with
        {
            NaturalPauseThreshold = TimeSpan.FromSeconds(30),
            PassiveBreakThreshold = TimeSpan.FromSeconds(10),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        SettingsValidationError error = Assert.Single(errors);
        Assert.Equal("NaturalPauseThreshold", error.Field);
    }

    [Fact]
    public void Both_pause_ordering_rules_are_reported_together()
    {
        // No in-range combination can break both rules at once, so the fixture also
        // trips two range errors; what matters is that neither ordering rule masks the
        // other.
        AppSettings settings = AppSettings.Default with
        {
            NaturalPauseThreshold = TimeSpan.FromSeconds(60),
            PassiveBreakThreshold = TimeSpan.FromSeconds(60),
            IdleThreshold = TimeSpan.FromSeconds(60),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e =>
            e.Field == "NaturalPauseThreshold" && e.Message.Contains("passive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e =>
            e.Field == "PassiveBreakThreshold" && e.Message.Contains("idle", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Defaults_debt_thresholds_are_20_35_45_60()
    {
        AppSettings settings = AppSettings.Default;
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Empty(errors);
        Assert.Equal(TimeSpan.FromMinutes(20), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(35), settings.DebtLevel2Threshold);
        Assert.Equal(TimeSpan.FromMinutes(45), settings.DebtLevel3Threshold);
        Assert.Equal(TimeSpan.FromMinutes(60), settings.DebtLevel4Threshold);
    }

    [Fact]
    public void Debt_level2_equal_to_work_interval_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(20),
            DebtLevel2Threshold = TimeSpan.FromMinutes(20),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "DebtLevel2Threshold");
    }

    [Fact]
    public void Debt_level3_equal_to_level2_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            DebtLevel2Threshold = TimeSpan.FromMinutes(35),
            DebtLevel3Threshold = TimeSpan.FromMinutes(35),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "DebtLevel3Threshold");
    }

    [Fact]
    public void Debt_level4_below_level3_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            DebtLevel3Threshold = TimeSpan.FromMinutes(45),
            DebtLevel4Threshold = TimeSpan.FromMinutes(44),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "DebtLevel4Threshold");
    }

    [Fact]
    public void Strictly_increasing_debt_thresholds_are_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(20),
            DebtLevel2Threshold = TimeSpan.FromMinutes(21),
            DebtLevel3Threshold = TimeSpan.FromMinutes(22),
            DebtLevel4Threshold = TimeSpan.FromMinutes(23),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, e => e.Field is "DebtLevel2Threshold" or "DebtLevel3Threshold" or "DebtLevel4Threshold");
    }

    [Fact]
    public void Work_interval_below_minimum_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(9),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "WorkInterval");
    }

    [Fact]
    public void Work_interval_at_minimum_and_maximum_are_valid()
    {
        var validator = new AppSettingsValidator();

        var atMin = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(10),
            DebtLevel2Threshold = TimeSpan.FromMinutes(11),
            DebtLevel3Threshold = TimeSpan.FromMinutes(12),
            DebtLevel4Threshold = TimeSpan.FromMinutes(13),
        };
        Assert.Empty(validator.Validate(atMin));

        var atMax = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(60),
            DebtLevel2Threshold = TimeSpan.FromMinutes(61),
            DebtLevel3Threshold = TimeSpan.FromMinutes(62),
            DebtLevel4Threshold = TimeSpan.FromMinutes(63),
        };
        Assert.Empty(validator.Validate(atMax));
    }

    [Fact]
    public void Maximum_reminder_wait_zero_is_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            MaximumReminderWait = TimeSpan.Zero,
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, e => e.Field == "MaximumReminderWait");
    }

    [Fact]
    public void Unknown_break_guide_mode_is_invalid()
    {
        AppSettings settings = AppSettings.Default with
        {
            BreakGuideMode = (BreakGuideMode)99,
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "BreakGuideMode");
    }

    [Fact]
    public void Passive_pause_one_second_below_idle_threshold_is_valid()
    {
        AppSettings settings = AppSettings.Default with
        {
            PassiveBreakThreshold = TimeSpan.FromSeconds(59),
            IdleThreshold = TimeSpan.FromMinutes(1),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, e => e.Field == "PassiveBreakThreshold");
    }

    [Fact]
    public void Multiple_violations_are_all_reported()
    {
        AppSettings settings = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(9),
            DebtLevel2Threshold = TimeSpan.FromMinutes(5),
        };
        var validator = new AppSettingsValidator();

        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);

        Assert.Contains(errors, e => e.Field == "WorkInterval");
        Assert.Contains(errors, e => e.Field == "DebtLevel2Threshold");
        Assert.True(errors.Count >= 2);
    }
}
