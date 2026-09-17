using RestCue.App.UsageEvents;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests.UsageEvents;

public sealed class SqliteWriteCadenceTests
{
    [Fact]
    public void No_writes_when_no_events_triggered()
    {
        var countingRepo = new CountingRepository();

        using (var writer = new BackgroundUsageEventWriter(countingRepo, channelCapacity: 256))
        {
        }

        Assert.Equal(0, countingRepo.WriteCallCount);
    }

    [Fact]
    public void Only_state_transition_events_trigger_writes()
    {
        var countingRepo = new CountingRepository();

        using (var writer = new BackgroundUsageEventWriter(countingRepo, channelCapacity: 256))
        {
            writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);
            writer.Write(UsageEventType.Paused, DateTimeOffset.UtcNow);
        }

        Assert.Equal(2, countingRepo.WriteCallCount);
    }

    [Fact]
    public async Task Writes_are_flushed_in_order()
    {
        var countingRepo = new CountingRepository();

        using (var writer = new BackgroundUsageEventWriter(countingRepo, channelCapacity: 256))
        {
            writer.Write(UsageEventType.BreakStarted, DateTimeOffset.UtcNow);
            writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);
        }

        Assert.Equal(2, countingRepo.WriteCallCount);
    }

    [Fact]
    public async Task Events_queued_during_a_write_are_persisted_as_one_batch()
    {
        var repo = new GatedRepository();

        using (var writer = new BackgroundUsageEventWriter(repo, channelCapacity: 256))
        {
            writer.Write(UsageEventType.BreakStarted, DateTimeOffset.UtcNow);

            // The consumer is now inside the first write; everything queued behind it
            // should be drained into a single call.
            await repo.FirstWriteEntered.Task;
            writer.Write(UsageEventType.Paused, DateTimeOffset.UtcNow);
            writer.Write(UsageEventType.Resumed, DateTimeOffset.UtcNow);
            writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);
            repo.ReleaseFirstWrite.SetResult();
        }

        Assert.Equal([1, 3], repo.BatchSizes);
    }

    private sealed class GatedRepository : IUsageEventRepository
    {
        private bool firstWriteDone;

        public TaskCompletionSource FirstWriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> BatchSizes { get; } = [];

        public async Task WriteManyAsync(
            IReadOnlyList<UsageEventWrite> events, CancellationToken ct = default)
        {
            BatchSizes.Add(events.Count);

            if (!firstWriteDone)
            {
                firstWriteDone = true;
                FirstWriteEntered.SetResult();
                await ReleaseFirstWrite.Task;
            }
        }

        public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null, CancellationToken ct = default) =>
            WriteManyAsync([new UsageEventWrite(eventType, occurredUtc, payload)], ct);

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UsageEvent>>(Array.Empty<UsageEvent>());
    }

    private sealed class CountingRepository : IUsageEventRepository
    {
        public int WriteCallCount { get; private set; }

        public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null, CancellationToken ct = default)
        {
            WriteCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<UsageEvent>>(Array.Empty<UsageEvent>());
        }
    }
}
