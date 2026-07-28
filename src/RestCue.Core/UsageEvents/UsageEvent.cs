namespace RestCue.Core.UsageEvents;

public sealed record UsageEvent(
    long Id,
    DateTimeOffset OccurredUtc,
    UsageEventType EventType,
    UsageEventPayload? Payload);
