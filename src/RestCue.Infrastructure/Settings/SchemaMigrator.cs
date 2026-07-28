using Microsoft.Data.Sqlite;

namespace RestCue.Infrastructure.Settings;

public static class SchemaMigrator
{
    public const int LatestSchemaVersion = 2;

    public static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        long version = await GetUserVersionAsync(connection, cancellationToken);

        if (version > LatestSchemaVersion)
            throw new UnsupportedSettingsSchemaException(version, LatestSchemaVersion);

        if (version == LatestSchemaVersion)
            return;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (version == 0)
            {
                await ExecuteNonQueryAsync(connection, CreateSettingsTableSql, cancellationToken);
                await ExecuteNonQueryAsync(connection, CreateUsageEventsTableSql, cancellationToken);
            }
            else if (version == 1)
            {
                await ExecuteNonQueryAsync(connection, CreateUsageEventsTableSql, cancellationToken);
            }

            await ExecuteNonQueryAsync(connection, CreateUsageEventsIndex1Sql, cancellationToken);
            await ExecuteNonQueryAsync(connection, CreateUsageEventsIndex2Sql, cancellationToken);

            await SetUserVersionAsync(connection, LatestSchemaVersion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<long> GetUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task SetUserVersionAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string CreateSettingsTableSql =
        """
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );
        """;

    private const string CreateUsageEventsTableSql =
        """
        CREATE TABLE IF NOT EXISTS usage_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            occurred_utc TEXT NOT NULL,
            event_type TEXT NOT NULL,
            payload TEXT
        );
        """;

    private const string CreateUsageEventsIndex1Sql =
        "CREATE INDEX IF NOT EXISTS idx_usage_events_occurred_utc ON usage_events (occurred_utc);";

    private const string CreateUsageEventsIndex2Sql =
        "CREATE INDEX IF NOT EXISTS idx_usage_events_type_time ON usage_events (event_type, occurred_utc);";
}
