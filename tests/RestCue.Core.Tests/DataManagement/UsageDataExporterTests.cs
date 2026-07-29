using System.Text.Json;
using RestCue.Core.DataManagement;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.Core.Tests.DataManagement;

public sealed class UsageDataExporterTests
{
    [Fact]
    public async Task Export_from_empty_database_writes_valid_empty_document()
    {
        var repo = new FakeUsageEventRepository();
        var writer = new FakeExportWriter();
        var exporter = new UsageDataExporter(repo, writer);

        var result = await exporter.ExportAsync("out.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.True(result.Succeeded);
        Assert.Equal("out.json", result.WrittenPath);

        var doc = JsonSerializer.Deserialize<UsageEventExportDocument>(writer.WrittenJson!);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.SchemaVersion);
        Assert.Empty(doc.Events);
    }

    [Fact]
    public async Task Export_contains_only_allowlist_fields()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        long id = 1;

        foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
        {
            UsageEventPayload? payload = type switch
            {
                UsageEventType.ReminderDismissed =>
                    new ReminderDismissedPayload(ReminderResult.Snoozed),
                UsageEventType.RestDebtLevelChanged =>
                    new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level1),
                _ => null
            };
            repo.Events.Add(new UsageEvent(id++, baseTime, type, payload));
        }

        var writer = new FakeExportWriter();
        var exporter = new UsageDataExporter(repo, writer);

        var result = await exporter.ExportAsync("out.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.True(result.Succeeded);

        string json = writer.WrittenJson!;
        Assert.DoesNotContain("processName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clipboard", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documentName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("screenContent", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_produces_consistent_results_across_calls()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderShown, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(1), UsageEventType.BreakCompleted, null));

        var writer1 = new FakeExportWriter();
        var exporter1 = new UsageDataExporter(repo, writer1);
        await exporter1.ExportAsync("out1.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        var writer2 = new FakeExportWriter();
        var exporter2 = new UsageDataExporter(repo, writer2);
        await exporter2.ExportAsync("out2.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        var doc1 = JsonSerializer.Deserialize<UsageEventExportDocument>(writer1.WrittenJson!);
        var doc2 = JsonSerializer.Deserialize<UsageEventExportDocument>(writer2.WrittenJson!);

        Assert.NotNull(doc1);
        Assert.NotNull(doc2);
        Assert.Equal(doc1.Events.Count, doc2.Events.Count);
        for (int i = 0; i < doc1.Events.Count; i++)
        {
            Assert.Equal(doc1.Events[i].Id, doc2.Events[i].Id);
            Assert.Equal(doc1.Events[i].EventType, doc2.Events[i].EventType);
        }
    }

    [Fact]
    public async Task Export_is_unaffected_by_process_name_opt_in()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderShown, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(1), UsageEventType.BreakCompleted, null));
        repo.Events.Add(new UsageEvent(3, baseTime.AddMinutes(2), UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.Snoozed)));
        repo.Events.Add(new UsageEvent(4, baseTime.AddMinutes(3), UsageEventType.RestDebtLevelChanged,
            new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level2)));

        var writer1 = new FakeExportWriter();
        var exporter1 = new UsageDataExporter(repo, writer1, sourceDatabaseSchemaVersion: 2);
        var result1 = await exporter1.ExportAsync("out1.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.True(result1.Succeeded);

        var writer2 = new FakeExportWriter();
        var exporter2 = new UsageDataExporter(repo, writer2, sourceDatabaseSchemaVersion: 2);
        var result2 = await exporter2.ExportAsync("out2.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.True(result2.Succeeded);

        var doc1 = JsonSerializer.Deserialize<UsageEventExportDocument>(writer1.WrittenJson!);
        var doc2 = JsonSerializer.Deserialize<UsageEventExportDocument>(writer2.WrittenJson!);
        Assert.NotNull(doc1);
        Assert.NotNull(doc2);

        var normalized1 = doc1 with { ExportedAtUtc = default };
        var normalized2 = doc2 with { ExportedAtUtc = default };

        string json1 = JsonSerializer.Serialize(normalized1);
        string json2 = JsonSerializer.Serialize(normalized2);
        Assert.Equal(json1, json2);
    }

    [Fact]
    public async Task Export_timestamps_are_utc_roundtrip()
    {
        var repo = new FakeUsageEventRepository();
        var nonUtcTime = new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.FromHours(8));
        repo.Events.Add(new UsageEvent(1, nonUtcTime, UsageEventType.ReminderShown, null));

        var writer = new FakeExportWriter();
        var exporter = new UsageDataExporter(repo, writer);

        await exporter.ExportAsync("out.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        string json = writer.WrittenJson!;

        var doc = JsonSerializer.Deserialize<UsageEventExportDocument>(json);
        Assert.NotNull(doc);
        Assert.Single(doc.Events);
        Assert.Equal(TimeSpan.Zero, doc.Events[0].OccurredUtc.Offset);
        Assert.Equal(nonUtcTime.ToUniversalTime(), doc.Events[0].OccurredUtc);

        Assert.Contains("2026-07-15T12:00:00", json);
        Assert.DoesNotContain("+08:00", json);
    }

    [Fact]
    public async Task Export_with_corrupt_event_row_reports_failure_not_success()
    {
        var repo = new FakeUsageEventRepository(throwOnQuery: true);
        var writer = new FakeExportWriter();
        var exporter = new UsageDataExporter(repo, writer);

        var result = await exporter.ExportAsync("out.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.False(result.Succeeded);
        Assert.Null(result.WrittenPath);
        Assert.Null(writer.WrittenJson);
    }

    [Fact]
    public async Task Export_failure_leaves_no_partial_file()
    {
        var repo = new FakeUsageEventRepository();
        repo.Events.Add(new UsageEvent(1, new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero), UsageEventType.ReminderShown, null));

        var failingWriter = new FakeExportWriter(throwOnWrite: true);
        var exporter = new UsageDataExporter(repo, failingWriter);

        var result = await exporter.ExportAsync("fail.json", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.False(result.Succeeded);
        Assert.Null(result.WrittenPath);
        Assert.Null(failingWriter.WrittenJson);
    }

    [Fact]
    public void MapToRecord_maps_ReminderDismissedPayload()
    {
        var ev = new UsageEvent(1, DateTimeOffset.UtcNow, UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.Snoozed));

        var record = UsageDataExporter.MapToRecord(ev);

        Assert.Equal(ReminderResult.Snoozed, record.DismissalResult);
        Assert.Null(record.DebtPrevious);
        Assert.Null(record.DebtCurrent);
    }

    [Fact]
    public void MapToRecord_maps_RestDebtLevelChangedPayload()
    {
        var ev = new UsageEvent(2, DateTimeOffset.UtcNow, UsageEventType.RestDebtLevelChanged,
            new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level2));

        var record = UsageDataExporter.MapToRecord(ev);

        Assert.Null(record.DismissalResult);
        Assert.Equal(RestDebtLevel.Level0, record.DebtPrevious);
        Assert.Equal(RestDebtLevel.Level2, record.DebtCurrent);
    }

    [Fact]
    public void MapToRecord_maps_null_payload()
    {
        var ev = new UsageEvent(3, DateTimeOffset.UtcNow, UsageEventType.BreakCompleted, null);

        var record = UsageDataExporter.MapToRecord(ev);

        Assert.Null(record.DismissalResult);
        Assert.Null(record.DebtPrevious);
        Assert.Null(record.DebtCurrent);
    }

    [Fact]
    public void SerializeDocument_round_trips()
    {
        var doc = new UsageEventExportDocument(
            1, 2,
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
            "Asia/Taipei",
            Array.Empty<UsageEventExportRecord>());

        string json = UsageDataExporter.SerializeDocument(doc);
        var deserialized = UsageDataExporter.DeserializeDocument(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.SchemaVersion);
        Assert.Equal(2, deserialized.SourceDatabaseSchemaVersion);
        Assert.Empty(deserialized.Events);
    }

    private sealed class FakeUsageEventRepository : IUsageEventRepository
    {
        private readonly bool throwOnQuery;

        public List<UsageEvent> Events { get; } = [];

        public FakeUsageEventRepository(bool throwOnQuery = false)
        {
            this.throwOnQuery = throwOnQuery;
        }

        public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            if (throwOnQuery)
                throw new InvalidOperationException("Simulated query failure.");

            var result = Events
                .Where(e => e.OccurredUtc >= from && e.OccurredUtc < to)
                .OrderBy(e => e.OccurredUtc)
                .ThenBy(e => e.Id)
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<UsageEvent>>(result);
        }
    }

    private sealed class FakeExportWriter : IExportWriter
    {
        private readonly bool throwOnWrite;

        public string? WrittenJson { get; private set; }

        public FakeExportWriter(bool throwOnWrite = false)
        {
            this.throwOnWrite = throwOnWrite;
        }

        public Task WriteAsync(string json, CancellationToken cancellationToken = default)
        {
            if (throwOnWrite)
                throw new IOException("Simulated write failure.");

            WrittenJson = json;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
