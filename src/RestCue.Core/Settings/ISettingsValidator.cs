namespace RestCue.Core.Settings;

public interface ISettingsValidator
{
    IReadOnlyList<SettingsValidationError> Validate(AppSettings settings);
}
