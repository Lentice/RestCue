namespace RestCue.Core.Settings;

public sealed class AppSettingsValidator : ISettingsValidator
{
    public IReadOnlyList<SettingsValidationError> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<SettingsValidationError>();
        AddRangeError(errors, settings.WorkInterval, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60), nameof(settings.WorkInterval));
        AddRangeError(errors, settings.NaturalPauseThreshold, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), nameof(settings.NaturalPauseThreshold));
        AddRangeError(errors, settings.MaximumReminderWait, TimeSpan.Zero, TimeSpan.FromMinutes(10), nameof(settings.MaximumReminderWait));
        AddRangeError(errors, settings.BreakDuration, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), nameof(settings.BreakDuration));
        AddRangeError(errors, settings.SnoozeDuration, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30), nameof(settings.SnoozeDuration));
        AddRangeError(errors, settings.RetryCooldown, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60), nameof(settings.RetryCooldown));
        AddRangeError(errors, settings.IdleThreshold, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10), nameof(settings.IdleThreshold));
        AddRangeError(errors, settings.PassiveBreakThreshold, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(120), nameof(settings.PassiveBreakThreshold));
        AddRangeError(errors, settings.ReminderDisplayDuration, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(120), nameof(settings.ReminderDisplayDuration));

        AddRangeError(errors, settings.DebtLevel2Threshold, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(240), nameof(settings.DebtLevel2Threshold));
        AddRangeError(errors, settings.DebtLevel3Threshold, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(240), nameof(settings.DebtLevel3Threshold));
        AddRangeError(errors, settings.DebtLevel4Threshold, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(240), nameof(settings.DebtLevel4Threshold));

        if (settings.ReminderOpacity is < 0.2 or > 1.0)
        {
            errors.Add(new(nameof(settings.ReminderOpacity), "Reminder opacity must be between 20% and 100%."));
        }

        if (settings.PassiveBreakThreshold >= settings.IdleThreshold)
        {
            errors.Add(new(
                nameof(settings.PassiveBreakThreshold),
                "Passive break threshold must be less than idle threshold."));
        }

        if (settings.WorkInterval >= settings.DebtLevel2Threshold)
        {
            errors.Add(new(
                nameof(settings.DebtLevel2Threshold),
                "Debt Level 2 threshold must be greater than Work Interval (Level 1)."));
        }

        if (settings.DebtLevel2Threshold >= settings.DebtLevel3Threshold)
        {
            errors.Add(new(
                nameof(settings.DebtLevel3Threshold),
                "Debt Level 3 threshold must be greater than Level 2."));
        }

        if (settings.DebtLevel3Threshold >= settings.DebtLevel4Threshold)
        {
            errors.Add(new(
                nameof(settings.DebtLevel4Threshold),
                "Debt Level 4 threshold must be greater than Level 3."));
        }

        if (!Enum.IsDefined(typeof(BreakGuideMode), settings.BreakGuideMode))
        {
            errors.Add(new(nameof(settings.BreakGuideMode), "Break guide mode is not a defined value."));
        }

        return errors;
    }

    private static void AddRangeError(
        ICollection<SettingsValidationError> errors,
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string field)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(new(field, $"{field} must be between {minimum} and {maximum}."));
        }
    }
}
