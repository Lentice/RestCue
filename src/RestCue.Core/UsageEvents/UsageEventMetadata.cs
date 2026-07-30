namespace RestCue.Core.UsageEvents;

public sealed record UsageEventMetadata(
    long TotalCount,
    DateTimeOffset? EarliestUtc,
    DateTimeOffset? LatestUtc,
    IReadOnlyDictionary<UsageEventType, long> PerTypeCounts,
    long UnparsableRowCount,
    long SchemaVersion,
    DateTimeOffset? LastExportUtc,
    DateTimeOffset? LastClearUtc);
