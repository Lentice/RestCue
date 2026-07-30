using System.Threading.Channels;
using RestCue.Core.UsageEvents;

namespace RestCue.App.UsageEvents;

public sealed class BackgroundUsageEventWriter : IDisposable
{
    private readonly IUsageEventRepository repository;
    private readonly Action<string>? onError;
    private readonly Channel<WriteRequest> channel;
    private readonly CancellationTokenSource cts;
    private readonly Task consumerTask;

    public BackgroundUsageEventWriter(
        IUsageEventRepository repository,
        Action<string>? onError = null,
        int channelCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (channelCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Must be at least 1.");

        this.repository = repository;
        this.onError = onError;
        channel = Channel.CreateBounded<WriteRequest>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite
            });
        cts = new CancellationTokenSource();
        consumerTask = ConsumeAsync(cts.Token);
    }

    public void Write(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload = null)
    {
        if (!channel.Writer.TryWrite(new WriteRequest(eventType, occurredUtc, payload)))
            onError?.Invoke("RestCue: usage event channel full; event dropped.");
    }

    public void Dispose()
    {
        channel.Writer.TryComplete();

        try
        {
            consumerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        if (!consumerTask.IsCompleted)
        {
            cts.Cancel();
            try
            {
                consumerTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await repository.WriteAsync(request.EventType, request.OccurredUtc, request.Payload, CancellationToken.None);
            }
            catch
            {
                onError?.Invoke("RestCue: failed to persist usage event.");
            }
        }
    }

    private sealed record WriteRequest(UsageEventType EventType, DateTimeOffset OccurredUtc, UsageEventPayload? Payload);
}
