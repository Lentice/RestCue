namespace RestCue.Core.UsageEvents;

public interface IUsageEventRepository
{
    Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists several events as one unit. The default implementation writes them one by one;
    /// a store with per-write overhead (a transaction, an open file) overrides it to pay that once.
    /// </summary>
    async Task WriteManyAsync(IReadOnlyList<UsageEventWrite> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var e in events)
            await WriteAsync(e.EventType, e.OccurredUtc, e.Payload, cancellationToken);
    }
}
