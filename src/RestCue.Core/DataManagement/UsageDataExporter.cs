using System.Text.Json;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;

namespace RestCue.Core.DataManagement;

public sealed class UsageDataExporter : IUsageDataExporter
{
    private readonly IUsageEventRepository repository;
    private readonly IExportWriter writer;
    private readonly int sourceDatabaseSchemaVersion;

    public UsageDataExporter(
        IUsageEventRepository repository,
        IExportWriter writer,
        int sourceDatabaseSchemaVersion = 2)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(writer);
        this.repository = repository;
        this.writer = writer;
        this.sourceDatabaseSchemaVersion = sourceDatabaseSchemaVersion;
    }

    public async Task<ExportResult> ExportAsync(
        string destinationPath,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<UsageEvent> events = await repository.QueryAsync(from, to, cancellationToken);

            var records = events.Select(MapToRecord).ToList();

            var document = new UsageEventExportDocument(
                SchemaVersion: 1,
                SourceDatabaseSchemaVersion: sourceDatabaseSchemaVersion,
                ExportedAtUtc: DateTimeOffset.UtcNow,
                ExportTimeZoneId: TimeZoneInfo.Local.Id,
                Events: records.AsReadOnly());

            string json = SerializeDocument(document);

            await writer.WriteAsync(json, cancellationToken);
            await writer.CommitAsync(cancellationToken);

            return new ExportResult(true, destinationPath, null);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, null, ex.Message);
        }
    }

    internal static UsageEventExportRecord MapToRecord(UsageEvent ev)
    {
        ReminderResult? dismissalResult = null;
        RestDebtLevel? debtPrevious = null;
        RestDebtLevel? debtCurrent = null;

        if (ev.Payload is ReminderDismissedPayload rdp)
        {
            dismissalResult = rdp.Result;
        }
        else if (ev.Payload is RestDebtLevelChangedPayload rlp)
        {
            debtPrevious = rlp.Previous;
            debtCurrent = rlp.Current;
        }

        return new UsageEventExportRecord(
            ev.Id,
            ev.OccurredUtc.ToUniversalTime(),
            ev.EventType.ToString(),
            dismissalResult,
            debtPrevious,
            debtCurrent);
    }

    internal static string SerializeDocument(UsageEventExportDocument document)
    {
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    internal static UsageEventExportDocument? DeserializeDocument(string json)
    {
        return JsonSerializer.Deserialize<UsageEventExportDocument>(json);
    }
}
