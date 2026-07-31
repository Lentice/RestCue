using Microsoft.Data.Sqlite;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
using Xunit;

namespace RestCue.Infrastructure.Tests.UsageEvents;

public sealed class SqliteUsageEventMetadataReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Empty_database_reports_zero_counts_and_no_range()
    {
        var (reader, _) = await CreateReaderAsync();
        var metadata = await reader.ReadMetadataAsync();

        Assert.Equal(0, metadata.TotalCount);
        Assert.Null(metadata.EarliestUtc);
        Assert.Null(metadata.LatestUtc);
        Assert.Equal(0, metadata.UnparsableRowCount);
    }

    [Fact]
    public async Task All_event_types_are_listed_from_enum_after_writing_all_types()
    {
        var (reader, dbPath) = await CreateReaderAsync();
        var repo = new SqliteUsageEventRepository(dbPath);
        var baseTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
        {
            UsageEventPayload? payload = type switch
            {
                UsageEventType.ReminderDismissed =>
                    new ReminderDismissedPayload(ReminderResult.Snoozed),
                UsageEventType.RestDebtLevelChanged =>
                    new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level1),
                UsageEventType.ForegroundProcessChanged =>
                    new ForegroundProcessChangedPayload("test-app"),
                UsageEventType.ErrorOccurred =>
                    new ErrorOccurredPayload("TestError"),
                _ => null
            };
            await repo.WriteAsync(type, baseTime, payload);
        }

        var metadata = await reader.ReadMetadataAsync();
        Assert.Equal(Enum.GetValues<UsageEventType>().Length, metadata.PerTypeCounts.Count);
    }

    [Fact]
    public async Task Counts_and_range_match_written_events()
    {
        var (reader, dbPath) = await CreateReaderAsync();

        var repo = new SqliteUsageEventRepository(dbPath);
        var t1 = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 6, 15, 11, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        await repo.WriteAsync(UsageEventType.ReminderShown, t1);
        await repo.WriteAsync(UsageEventType.BreakCompleted, t2);
        await repo.WriteAsync(UsageEventType.BreakStarted, t3);

        var metadata = await reader.ReadMetadataAsync();

        Assert.Equal(3, metadata.TotalCount);
        Assert.Equal(t1, metadata.EarliestUtc);
        Assert.Equal(t3, metadata.LatestUtc);
    }

    [Fact]
    public async Task Unparsable_row_is_counted_not_thrown()
    {
        var (reader, dbPath) = await CreateReaderAsync();

        var repo = new SqliteUsageEventRepository(dbPath);
        var validTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.Paused, validTime);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO usage_events (occurred_utc, event_type, payload)
                VALUES ('not-a-date', 'ReminderShown', NULL);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var metadata = await reader.ReadMetadataAsync();

        Assert.Equal(2, metadata.TotalCount);
        Assert.Equal(1, metadata.UnparsableRowCount);
    }

    [Fact]
    public async Task Malformed_payload_row_does_not_break_metadata()
    {
        var (reader, dbPath) = await CreateReaderAsync();

        var repo = new SqliteUsageEventRepository(dbPath);
        var validTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.Paused, validTime);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO usage_events (occurred_utc, event_type, payload)
                VALUES ($time, 'ReminderShown', 'not valid json{{{');
                """;
            cmd.Parameters.AddWithValue("$time", validTime.AddHours(1).ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        var metadata = await reader.ReadMetadataAsync();

        Assert.Equal(2, metadata.TotalCount);
        Assert.Equal(2, metadata.PerTypeCounts.Values.Sum());
    }

    [Fact]
    public async Task Unknown_event_type_string_is_counted_as_unparsable()
    {
        var (_, dbPath) = await CreateReaderAsync();

        var validTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO usage_events (occurred_utc, event_type, payload)
                VALUES ($time, 'FutureEvent', NULL);
                """;
            cmd.Parameters.AddWithValue("$time", validTime.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        var (reader2, _) = await CreateReaderAsync();
        var metadata = await reader2.ReadMetadataAsync();

        Assert.Equal(1, metadata.UnparsableRowCount);
        Assert.Equal(0, metadata.PerTypeCounts.Values.Sum());
    }

    [Fact]
    public async Task Opening_snapshot_writes_nothing()
    {
        var (reader, dbPath) = await CreateReaderAsync();

        var repo = new SqliteUsageEventRepository(dbPath);
        await repo.WriteAsync(UsageEventType.Enabled,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await repo.WriteAsync(UsageEventType.Disabled,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        long userVersionBefore;
        long countBefore;
        string? settingsUpdatedAtBefore;
        long fileLengthBefore;
        DateTime lastWriteBefore;

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                userVersionBefore = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM usage_events;";
                countBefore = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT updated_at_utc FROM settings WHERE key = 'app_settings';";
                settingsUpdatedAtBefore = (await cmd.ExecuteScalarAsync()) as string;
            }
        }

        var fileInfo = new FileInfo(dbPath);
        fileLengthBefore = fileInfo.Length;
        lastWriteBefore = fileInfo.LastWriteTimeUtc;

        for (int i = 0; i < 3; i++)
        {
            await reader.ReadMetadataAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                Assert.Equal(userVersionBefore, await cmd.ExecuteScalarAsync());
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM usage_events;";
                Assert.Equal(countBefore, await cmd.ExecuteScalarAsync());
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT updated_at_utc FROM settings WHERE key = 'app_settings';";
                Assert.Equal(settingsUpdatedAtBefore, await cmd.ExecuteScalarAsync());
            }
        }

        fileInfo = new FileInfo(dbPath);
        Assert.Equal(fileLengthBefore, fileInfo.Length);
        Assert.Equal(lastWriteBefore, fileInfo.LastWriteTimeUtc);

        var walFiles = Directory.GetFiles(directory, "*.wal");
        var shmFiles = Directory.GetFiles(directory, "*.shm");
        var bakFiles = Directory.GetFiles(directory, "*.bak");
        Assert.Empty(walFiles);
        Assert.Empty(shmFiles);
        Assert.Empty(bakFiles);
    }

    [Fact]
    public async Task ReadOnly_mode_rejects_insert()
    {
        var (reader, dbPath) = await CreateReaderAsync();

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString();

        await using var connection = new SqliteConnection(connStr);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO usage_events (occurred_utc, event_type, payload)
            VALUES ('2026-01-01T00:00:00.0000000Z', 'ReminderShown', NULL);
            """;
        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }

    private async Task<(IUsageEventMetadataReader reader, string dbPath)> CreateReaderAsync()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);

        var reader = new SqliteUsageEventMetadataReader(dbPath);
        return (reader, dbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
