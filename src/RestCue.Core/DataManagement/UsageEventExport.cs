using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;

namespace RestCue.Core.DataManagement;

public sealed record UsageEventExportRecord(
    long Id,
    DateTimeOffset OccurredUtc,
    string EventType,
    ReminderResult? DismissalResult,
    RestDebtLevel? DebtPrevious,
    RestDebtLevel? DebtCurrent);

public sealed record UsageEventExportDocument(
    int SchemaVersion,
    int SourceDatabaseSchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string ExportTimeZoneId,
    IReadOnlyList<UsageEventExportRecord> Events);
