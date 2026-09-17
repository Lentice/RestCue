namespace RestCue.Core.UsageEvents;

/// <summary>One pending usage-event write, as handed to <see cref="IUsageEventRepository.WriteManyAsync"/>.</summary>
public sealed record UsageEventWrite(
    UsageEventType EventType,
    DateTimeOffset OccurredUtc,
    UsageEventPayload? Payload = null);
