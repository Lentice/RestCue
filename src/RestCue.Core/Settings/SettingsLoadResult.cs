namespace RestCue.Core.Settings;

/// <summary>
/// Result of loading settings from the repository.
/// </summary>
/// <param name="Settings">The loaded settings (defaults if recovery occurred).</param>
/// <param name="RecoveredFromCorruption">
/// True when the database was recreated due to corruption (SQLite error 11/26)
/// OR the settings document was reset to defaults due to invalid JSON/validation failure.
/// In both cases the application should treat this as a one-time recovery notification.
/// When true, previously stored usage events may or may not be intact:
/// - Database corruption: usage events are lost (full backup and recreate).
/// - Settings document only: usage events are preserved.
/// </param>
/// <param name="CorruptBackupPath">
/// Path to a backup of the original corrupt database file.
/// Only non-null when database-level corruption (SQLite error 11/26) was detected.
/// Null when recovery was settings-document only.
/// </param>
public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RecoveredFromCorruption = false,
    string? CorruptBackupPath = null);
