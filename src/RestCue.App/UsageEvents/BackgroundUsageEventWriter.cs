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
                FullMode = BoundedChannelFullMode.Wait
            });
        cts = new CancellationTokenSource();
        consumerTask = ConsumeAsync(cts.Token);
    }

    public void Write(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload = null)
    {
        if (!channel.Writer.TryWrite(WriteRequest.Event(eventType, occurredUtc, payload)))
            onError?.Invoke("RestCue: usage event channel full; event dropped.");
    }

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(
            WriteRequest.Exclusive(async () =>
            {
                completion.TrySetResult(await operation());
            }, completion));
        return (T)(await completion.Task.ConfigureAwait(false))!;
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
                if (request.Operation != null)
                {
                    try
                    {
                        await request.Operation();
                    }
                    catch (Exception exception)
                    {
                        request.Completion!.TrySetException(exception);
                    }

                    continue;
                }

                await repository.WriteAsync(request.EventType!.Value, request.OccurredUtc, request.Payload, ct);
            }
            catch
            {
                onError?.Invoke("RestCue: failed to persist usage event.");
            }
        }
    }

    private sealed record WriteRequest(
        UsageEventType? EventType,
        DateTimeOffset OccurredUtc,
        UsageEventPayload? Payload,
        Func<Task>? Operation,
        TaskCompletionSource<object?>? Completion)
    {
        public static WriteRequest Event(
            UsageEventType eventType,
            DateTimeOffset occurredUtc,
            UsageEventPayload? payload) =>
            new(eventType, occurredUtc, payload, null, null);

        public static WriteRequest Exclusive(
            Func<Task> operation,
            TaskCompletionSource<object?> completion) =>
            new(null, default, null, operation, completion);
    }
}
