using Microsoft.Data.Sqlite;
using RestCue.Infrastructure.Settings;
using Xunit;

namespace RestCue.Infrastructure.Tests.Settings;

public sealed class SchemaMigratorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Fresh_database_creates_both_tables_at_latest_schema()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);

        long version = await SchemaMigrator.GetUserVersionAsync(connection, default);
        Assert.Equal(3, version);

        var tables = await GetTableNames(connection);
        Assert.Contains("settings", tables);
        Assert.Contains("usage_events", tables);
        Assert.Contains("application_rules", tables);
    }

    [Fact]
    public async Task V2_database_upgrades_to_v3_and_preserves_settings()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS usage_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_utc TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_usage_events_occurred_utc ON usage_events (occurred_utc);
                CREATE INDEX IF NOT EXISTS idx_usage_events_type_time ON usage_events (event_type, occurred_utc);
                PRAGMA user_version = 2;
                """;
            await cmd.ExecuteNonQueryAsync();

            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                "INSERT INTO settings (key, value, updated_at_utc) VALUES ('app_settings', '{}', '2026-01-01T00:00:00Z');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            long version = await SchemaMigrator.GetUserVersionAsync(connection, default);
            Assert.Equal(3, version);

            var tables = await GetTableNames(connection);
            Assert.Contains("application_rules", tables);

            await using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT value FROM settings WHERE key = 'app_settings';";
            var value = await selectCmd.ExecuteScalarAsync();
            Assert.Equal("{}", value);
        }
    }

    [Fact]
    public async Task V1_database_upgrades_to_v3_and_preserves_settings()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            await cmd.ExecuteNonQueryAsync();

            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                "INSERT INTO settings (key, value, updated_at_utc) VALUES ('app_settings', '{}', '2026-01-01T00:00:00Z');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            long version = await SchemaMigrator.GetUserVersionAsync(connection, default);
            Assert.Equal(3, version);

            var tables = await GetTableNames(connection);
            Assert.Contains("usage_events", tables);
            Assert.Contains("application_rules", tables);

            await using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT value FROM settings WHERE key = 'app_settings';";
            var value = await selectCmd.ExecuteScalarAsync();
            Assert.Equal("{}", value);
        }
    }

    [Fact]
    public async Task Repeated_startup_is_idempotent()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        for (int i = 0; i < 3; i++)
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        await using var verifyConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await verifyConnection.OpenAsync();
        long version = await SchemaMigrator.GetUserVersionAsync(verifyConnection, default);
        Assert.Equal(3, version);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_without_changes()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 99;";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(
                () => SchemaMigrator.EnsureSchemaAsync(connection));
        }

        await using var verifyConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await verifyConnection.OpenAsync();
        long version = await SchemaMigrator.GetUserVersionAsync(verifyConnection, default);
        Assert.Equal(99, version);

        var tables = await GetTableNames(verifyConnection);
        Assert.DoesNotContain("usage_events", tables);
        Assert.DoesNotContain("application_rules", tables);
    }

    [Fact]
    public async Task Usage_events_table_has_required_columns()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'usage_events';";
        string schema = Assert.IsType<string>(await cmd.ExecuteScalarAsync());
        Assert.Contains("id INTEGER PRIMARY KEY AUTOINCREMENT", schema);
        Assert.Contains("occurred_utc TEXT NOT NULL", schema);
        Assert.Contains("event_type TEXT NOT NULL", schema);
        Assert.Contains("payload TEXT", schema);
    }

    [Fact]
    public async Task Usage_events_indexes_are_created()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'usage_events' ORDER BY name;";
        var indexes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("idx_usage_events_occurred_utc", indexes);
        Assert.Contains("idx_usage_events_type_time", indexes);
    }

    private static async Task<List<string>> GetTableNames(SqliteConnection connection)
    {
        var names = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
