using RestCue.App.UsageEvents;
using RestCue.Core.DataManagement;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// The clear + metadata-timestamp composition that runs inside the event writer's
/// exclusive channel, so a clear that succeeded cannot be reported failed because its
/// metadata write raced a background event write.
/// </summary>
public sealed class DataManagementTimestampTests
{
    [Fact]
    public async Task ClearAndRecordAsync_records_timestamp_when_clear_succeeds()
    {
        var steps = new List<string>();

        ClearResult result = await App.ClearAndRecordAsync(
            () =>
            {
                steps.Add("clear");
                return Task.FromResult(new ClearResult(true, 42, null));
            },
            () =>
            {
                steps.Add("record");
                return Task.CompletedTask;
            });

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.AffectedRowCount);
        Assert.Equal(["clear", "record"], steps);
    }

    [Fact]
    public async Task ClearAndRecordAsync_does_not_record_timestamp_when_clear_fails()
    {
        var recordCalled = false;

        ClearResult result = await App.ClearAndRecordAsync(
            () => Task.FromResult(new ClearResult(false, 0, "simulated failure")),
            () =>
            {
                recordCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(result.Succeeded);
        Assert.Equal("simulated failure", result.ErrorMessage);
        Assert.False(recordCalled);
    }

    [Fact]
    public async Task ClearAndRecordAsync_runs_both_steps_inside_exclusive_channel()
    {
        var order = new List<string>();
        var fake = new FakeUsageEventRepository(onWrite: () => order.Add("event"));
        using var writer = new BackgroundUsageEventWriter(fake);

        writer.Write(UsageEventType.BreakStarted, DateTimeOffset.UtcNow);
        ClearResult result = await writer.RunExclusiveAsync(() => App.ClearAndRecordAsync(
            async () =>
            {
                order.Add("clear");
                await Task.Yield();
                return new ClearResult(true, 10, null);
            },
            () =>
            {
                order.Add("record");
                return Task.CompletedTask;
            }));
        writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);

        writer.Dispose();

        Assert.True(result.Succeeded);
        Assert.Equal(["event", "clear", "record", "event"], order);
    }

    private sealed class FakeUsageEventRepository : IUsageEventRepository
    {
        private readonly Action? onWrite;
        private readonly object syncRoot = new();

        public FakeUsageEventRepository(Action? onWrite = null)
        {
            this.onWrite = onWrite;
        }

        public Task WriteAsync(
            UsageEventType eventType,
            DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null,
            CancellationToken ct = default)
        {
            lock (syncRoot)
            {
                Writes.Add(new UsageEvent(0, occurredUtc, eventType, payload));
            }

            onWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public List<UsageEvent> Writes { get; } = [];
    }
}
