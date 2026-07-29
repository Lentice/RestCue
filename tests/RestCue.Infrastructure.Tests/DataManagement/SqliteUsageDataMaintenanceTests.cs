using Microsoft.Data.Sqlite;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
using Xunit;

namespace RestCue.Infrastructure.Tests.DataManagement;

public sealed class SqliteUsageDataMaintenanceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Clear_history_only_removes_events_and_keeps_settings()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        var baseTime = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.ReminderShown, baseTime);
        await repo.WriteAsync(UsageEventType.BreakCompleted, baseTime.AddMinutes(1));
        await repo.WriteAsync(UsageEventType.BreakCompleted, baseTime.AddMinutes(2));
        await repo.WriteAsync(UsageEventType.Paused, baseTime.AddMinutes(3));
        await repo.WriteAsync(UsageEventType.Resumed, baseTime.AddMinutes(4));

        var settingsRepo = new SqliteSettingsRepository(dbPath, new Core.Settings.AppSettingsValidator());
        var nonDefault = Core.Settings.AppSettings.Default with { CollectForegroundProcessNames = true };
        await settingsRepo.SaveAsync(nonDefault);

        var maintenance = new SqliteUsageDataMaintenance(dbPath);
        var result = await maintenance.ClearUsageHistoryAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.AffectedRowCount);

        var events = await repo.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Empty(events);

        var settingsResult = await settingsRepo.LoadAsync();
        Assert.True(settingsResult.Settings.CollectForegroundProcessNames);
        Assert.False(settingsResult.RecoveredFromCorruption);
    }

    [Fact]
    public async Task Clear_history_does_not_delete_database_file_or_downgrade_schema()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        long schemaVersionBefore;
        await using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            schemaVersionBefore = await SchemaMigrator.GetUserVersionAsync(conn, default);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        await repo.WriteAsync(UsageEventType.Enabled, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var maintenance = new SqliteUsageDataMaintenance(dbPath);
        await maintenance.ClearUsageHistoryAsync();

        Assert.True(File.Exists(dbPath));

        long schemaVersionAfter;
        await using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            schemaVersionAfter = await SchemaMigrator.GetUserVersionAsync(conn, default);
        }

        Assert.Equal(schemaVersionBefore, schemaVersionAfter);

        // usage_events table still exists and can be written to
        await repo.WriteAsync(UsageEventType.Disabled, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var events = await repo.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Single(events);
    }

    [Fact]
    public async Task Clear_history_rolls_back_when_database_locked()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        await repo.WriteAsync(UsageEventType.Enabled, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        int bakCountBefore = Directory.GetFiles(directory, "*.bak").Length;

        using (var lockConnection = new SqliteConnection($"Data Source={dbPath};Pooling=False;Default Timeout=1"))
        {
            await lockConnection.OpenAsync();
            await using var lockCmd = lockConnection.CreateCommand();
            lockCmd.CommandText = "BEGIN EXCLUSIVE;";
            await lockCmd.ExecuteNonQueryAsync();

            var maintenance = new SqliteUsageDataMaintenance(dbPath);
            var result = await maintenance.ClearUsageHistoryAsync();
            Assert.False(result.Succeeded);
            Assert.Equal(0, result.AffectedRowCount);
        }

        Assert.Equal(bakCountBefore, Directory.GetFiles(directory, "*.bak").Length);

        var events = await repo.QueryAsync(
            new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Single(events);
    }

    [Fact]
    public async Task Clear_history_with_unparsable_rows_still_succeeds()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);

            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                """
                INSERT INTO usage_events (occurred_utc, event_type, payload)
                VALUES ('2026-01-01T00:00:00.0000000Z', 'FutureEvent', 'not valid json{{{');
                """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        var maintenance = new SqliteUsageDataMaintenance(dbPath);
        var result = await maintenance.ClearUsageHistoryAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AffectedRowCount);

        var repo = new SqliteUsageEventRepository(dbPath);
        var events = await repo.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Clear_settings_only_resets_settings_and_keeps_events()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        var baseTime = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 5; i++)
        {
            await repo.WriteAsync(UsageEventType.ReminderShown, baseTime.AddMinutes(i));
        }

        var settingsRepo = new SqliteSettingsRepository(dbPath, new Core.Settings.AppSettingsValidator());
        var nonDefault = Core.Settings.AppSettings.Default with { CollectForegroundProcessNames = true };
        await settingsRepo.SaveAsync(nonDefault);

        await settingsRepo.SaveAsync(Core.Settings.AppSettings.Default);

        var events = await repo.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal(5, events.Count);

        var settingsResult = await settingsRepo.LoadAsync();
        Assert.Equal(2, settingsResult.Settings.SchemaVersion);
        Assert.False(settingsResult.RecoveredFromCorruption);
    }

    [Fact]
    public async Task Reset_settings_restores_process_name_opt_in_to_false()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var settingsRepo = new SqliteSettingsRepository(dbPath, new Core.Settings.AppSettingsValidator());
        var nonDefault = Core.Settings.AppSettings.Default with { CollectForegroundProcessNames = true };
        await settingsRepo.SaveAsync(nonDefault);

        await settingsRepo.SaveAsync(Core.Settings.AppSettings.Default);

        var settingsResult = await settingsRepo.LoadAsync();
        Assert.False(settingsResult.Settings.CollectForegroundProcessNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
