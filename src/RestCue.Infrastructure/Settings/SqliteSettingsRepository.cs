using System.Text.Json;
using Microsoft.Data.Sqlite;
using RestCue.Core.Settings;

namespace RestCue.Infrastructure.Settings;

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;
    private const string AppSettingsKey = "app_settings";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly ISettingsValidator validator;

    public SqliteSettingsRepository(string databasePath, ISettingsValidator validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString();
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectoryExists();

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key;";
            command.Parameters.AddWithValue("$key", AppSettingsKey);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is not string serializedSettings)
            {
                return new(AppSettings.Default);
            }

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
                serializedSettings,
                SerializerOptions) ?? throw new JsonException("The settings row contains no settings object.");
            settings = UpgradeDocument(settings);
            EnsureValid(settings);
            return new(settings);
        }
        catch (Exception exception) when (IsDatabaseCorrupt(exception))
        {
            return await RecoverFromCorruptionAsync(cancellationToken);
        }
        catch (Exception exception) when (IsSettingsDocumentCorrupt(exception))
        {
            return await RecoverSettingsOnlyAsync(cancellationToken);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureValid(settings);
        EnsureDirectoryExists();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value, updated_at_utc)
            VALUES ($key, $value, $updatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$key", AppSettingsKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, SerializerOptions));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SettingsLoadResult> RecoverSettingsOnlyAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value, updated_at_utc)
            VALUES ($key, $value, $updatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$key", AppSettingsKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(AppSettings.Default, SerializerOptions));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new(AppSettings.Default, RecoveredFromCorruption: true);
    }

    private async Task<SettingsLoadResult> RecoverFromCorruptionAsync(CancellationToken cancellationToken)
    {
        string backupPath = CreateCorruptBackupPath();
        File.Copy(databasePath, backupPath);
        File.Delete(databasePath);
        File.Delete($"{databasePath}-wal");
        File.Delete($"{databasePath}-shm");

        await SaveAsync(AppSettings.Default, cancellationToken);
        return new(AppSettings.Default, RecoveredFromCorruption: true, backupPath);
    }

    private static bool IsDatabaseCorrupt(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase };

    private static bool IsSettingsDocumentCorrupt(Exception exception) =>
        exception is JsonException or NotSupportedException or SettingsValidationException;

    private static AppSettings UpgradeDocument(AppSettings settings)
    {
        return settings with { SchemaVersion = 2 };
    }

    private void EnsureValid(AppSettings settings)
    {
        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }
    }

    private void EnsureDirectoryExists()
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private string CreateCorruptBackupPath() =>
        $"{databasePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
}
