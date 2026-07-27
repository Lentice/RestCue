namespace RestCue.Core.Settings;

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(IReadOnlyList<SettingsValidationError> errors)
        : base("The settings contain invalid values.")
    {
        Errors = errors;
    }

    public IReadOnlyList<SettingsValidationError> Errors { get; }
}
