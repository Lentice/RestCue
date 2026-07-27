namespace RestCue.Core.Settings;

public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RecoveredFromCorruption = false,
    string? CorruptBackupPath = null);
