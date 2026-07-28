namespace RestCue.Core.UsageEvents;

public interface IUsageEventRepository
{
    Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
