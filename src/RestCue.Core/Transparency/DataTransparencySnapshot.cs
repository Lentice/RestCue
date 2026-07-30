namespace RestCue.Core.Transparency;

using UsageEvents;

public enum CollectionState
{
    NeverCollected,
    DisabledByUser,
    EnabledEmpty,
    EnabledWithData,
    Unavailable
}

public sealed record UsageEventTypeCount(
    UsageEventType EventType,
    long Count);

public sealed record DataCategoryStatus(
    string Label,
    CollectionState State,
    string? Detail);

public sealed record DataTransparencySnapshot(
    IReadOnlyList<DataCategoryStatus> Categories,
    IReadOnlyList<UsageEventTypeCount> EventTypeCounts,
    long TotalEventCount,
    DateTimeOffset? EarliestUtc,
    DateTimeOffset? LatestUtc,
    long? DatabaseSizeBytes,
    DateTimeOffset? LastExportUtc,
    DateTimeOffset? LastClearUtc,
    string? UnavailableMessage);
