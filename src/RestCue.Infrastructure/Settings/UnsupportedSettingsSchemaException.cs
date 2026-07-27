namespace RestCue.Infrastructure.Settings;

public sealed class UnsupportedSettingsSchemaException : Exception
{
    public UnsupportedSettingsSchemaException(long actualVersion, int supportedVersion)
        : base($"Settings schema version {actualVersion} is newer than supported version {supportedVersion}.")
    {
        ActualVersion = actualVersion;
        SupportedVersion = supportedVersion;
    }

    public long ActualVersion { get; }

    public int SupportedVersion { get; }
}
