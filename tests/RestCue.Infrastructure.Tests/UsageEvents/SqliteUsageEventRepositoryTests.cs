using System.Text.Json;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
using Xunit;

namespace RestCue.Infrastructure.Tests.UsageEvents;

public sealed class SqliteUsageEventRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_and_query_single_event()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var occurred = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await repo.WriteAsync(UsageEventType.ReminderShown, occurred);

        var events = await repo.QueryAsync(
            occurred.AddMinutes(-1), occurred.AddMinutes(1));

        Assert.Single(events);
        Assert.Equal(UsageEventType.ReminderShown, events[0].EventType);
        Assert.Equal(occurred, events[0].OccurredUtc);
        Assert.Null(events[0].Payload);
    }

    [Fact]
    public async Task Write_and_query_all_event_types()
    {
        var (repo, _) = await CreateRepositoryAsync();
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
                _ => null
            };
            await repo.WriteAsync(type, baseTime, payload);
        }

        var events = await repo.QueryAsync(baseTime.AddHours(-1), baseTime.AddHours(1));
        Assert.Equal(Enum.GetValues<UsageEventType>().Length, events.Count);
    }

    [Fact]
    public async Task Write_event_with_ReminderDismissed_payload()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var occurred = new DateTimeOffset(2026, 6, 15, 14, 30, 0, TimeSpan.Zero);

        await repo.WriteAsync(
            UsageEventType.ReminderDismissed, occurred,
            new ReminderDismissedPayload(ReminderResult.Snoozed));

        var events = await repo.QueryAsync(occurred.AddMinutes(-1), occurred.AddMinutes(1));
        Assert.Single(events);
        var p = Assert.IsType<ReminderDismissedPayload>(events[0].Payload);
        Assert.Equal(ReminderResult.Snoozed, p.Result);
    }

    [Fact]
    public async Task RestDebtLevelChanged_payload_round_trips()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var occurred = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

        await repo.WriteAsync(
            UsageEventType.RestDebtLevelChanged, occurred,
            new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level2));

        var events = await repo.QueryAsync(occurred.AddMinutes(-1), occurred.AddMinutes(1));
        Assert.Single(events);
        var p = Assert.IsType<RestDebtLevelChangedPayload>(events[0].Payload);
        Assert.Equal(RestDebtLevel.Level0, p.Previous);
        Assert.Equal(RestDebtLevel.Level2, p.Current);
    }

    [Fact]
    public async Task UTC_timestamp_round_trips_correctly()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var occurred = new DateTimeOffset(2026, 12, 25, 23, 59, 59, 999, TimeSpan.FromHours(8));

        await repo.WriteAsync(UsageEventType.BreakCompleted, occurred);

        var events = await repo.QueryAsync(occurred.AddDays(-1), occurred.AddDays(1));
        Assert.Single(events);
        Assert.Equal(occurred, events[0].OccurredUtc);
    }

    [Fact]
    public async Task Non_Utc_offset_is_normalised_to_Utc()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var localTime = new DateTimeOffset(2026, 7, 4, 10, 30, 0, TimeSpan.FromHours(-5));
        var expectedUtc = localTime.ToUniversalTime();

        await repo.WriteAsync(UsageEventType.ReminderShown, localTime);

        var events = await repo.QueryAsync(expectedUtc.AddDays(-1), expectedUtc.AddDays(1));
        Assert.Single(events);
        Assert.Equal(expectedUtc, events[0].OccurredUtc);
        Assert.Equal(TimeSpan.Zero, events[0].OccurredUtc.Offset);
    }

    [Fact]
    public async Task Query_boundary_is_normalised_to_Utc()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var eventTime = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.Resumed, eventTime);

        var from = new DateTimeOffset(2026, 5, 15, 7, 0, 0, TimeSpan.FromHours(-5));
        var to = new DateTimeOffset(2026, 5, 15, 8, 0, 0, TimeSpan.FromHours(-5));

        var events = await repo.QueryAsync(from, to);
        Assert.Single(events);
        Assert.Equal(eventTime, events[0].OccurredUtc);
    }

    [Fact]
    public async Task Query_with_deterministic_ordering()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var baseTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await repo.WriteAsync(UsageEventType.Paused, baseTime);
        await repo.WriteAsync(UsageEventType.Resumed, baseTime);
        await repo.WriteAsync(UsageEventType.Paused, baseTime);

        var events = await repo.QueryAsync(baseTime.AddDays(-1), baseTime.AddDays(1));
        Assert.Equal(3, events.Count);

        long prevId = -1;
        foreach (var e in events)
        {
            Assert.True(e.Id > prevId);
            prevId = e.Id;
        }
    }

    [Fact]
    public async Task Query_outside_range_returns_empty()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var occurred = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.Disabled, occurred);

        var events = await repo.QueryAsync(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Assert.Empty(events);
    }

    [Fact]
    public async Task Multiple_events_persist_and_survive_repository_reopen()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo1 = new SqliteUsageEventRepository(dbPath);
        var baseTime = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);
        await repo1.WriteAsync(UsageEventType.ReminderShown, baseTime);
        await repo1.WriteAsync(UsageEventType.BreakCompleted, baseTime.AddMinutes(1));

        var repo2 = new SqliteUsageEventRepository(dbPath);
        var events = await repo2.QueryAsync(baseTime.AddDays(-1), baseTime.AddDays(1));

        Assert.Equal(2, events.Count);
        Assert.Equal(UsageEventType.ReminderShown, events[0].EventType);
        Assert.Equal(UsageEventType.BreakCompleted, events[1].EventType);
    }

    [Fact]
    public async Task Payload_does_not_contain_forbidden_fields()
    {
        var (repo, _) = await CreateRepositoryAsync();
        var baseTime = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

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
                _ => null
            };

            await repo.WriteAsync(type, baseTime, payload);
        }

        var events = await repo.QueryAsync(baseTime.AddDays(-1), baseTime.AddDays(1));

        string json;
        foreach (var e in events)
        {
            if (e.Payload is ReminderDismissedPayload rdp)
            {
                json = JsonSerializer.Serialize(new { result = rdp.Result.ToString() });
                Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("clipboard", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
            }
            else if (e.Payload is RestDebtLevelChangedPayload rlp)
            {
                json = JsonSerializer.Serialize(new { previous = rlp.Previous.ToString(), current = rlp.Current.ToString() });
                Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("clipboard", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
            }
            else if (e.Payload is ForegroundProcessChangedPayload fcp)
            {
                json = JsonSerializer.Serialize(new { processName = fcp.ProcessName });
                Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("clipboard", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Single_malformed_event_does_not_corrupt_database()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        var (repo, _) = await CreateRepositoryAsync();
        var validEventTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repo.WriteAsync(UsageEventType.Paused, validEventTime);

        var malformedTime = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO usage_events (occurred_utc, event_type, payload)
                VALUES ($time, 'ReminderShown', 'not valid json{{{');
                """;
            cmd.Parameters.AddWithValue("$time", malformedTime.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<JsonException>(
            () => repo.QueryAsync(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var validEvents = await repo.QueryAsync(
            validEventTime.AddMinutes(-1),
            validEventTime.AddMinutes(1));
        Assert.Single(validEvents);
        Assert.Equal(UsageEventType.Paused, validEvents[0].EventType);
    }

    [Fact]
    public async Task Operational_failure_does_not_trigger_database_recovery()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        await repo.WriteAsync(UsageEventType.Enabled, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        int bakCountBefore = Directory.GetFiles(directory, "*.bak").Length;

        using (var lockConnection = new Microsoft.Data.Sqlite.SqliteConnection(
                   $"Data Source={dbPath};Pooling=False;Default Timeout=1"))
        {
            await lockConnection.OpenAsync();
            await using var lockCmd = lockConnection.CreateCommand();
            lockCmd.CommandText = "BEGIN EXCLUSIVE;";
            await lockCmd.ExecuteNonQueryAsync();

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => repo.WriteAsync(UsageEventType.Disabled, new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero)));
        }

        Assert.Equal(bakCountBefore, Directory.GetFiles(directory, "*.bak").Length);

        var events = await repo.QueryAsync(
            new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Single(events);
        Assert.Equal(UsageEventType.Enabled, events[0].EventType);
    }

    private async Task<(IUsageEventRepository repo, string dbPath)> CreateRepositoryAsync()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);

        return (new SqliteUsageEventRepository(dbPath), dbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
