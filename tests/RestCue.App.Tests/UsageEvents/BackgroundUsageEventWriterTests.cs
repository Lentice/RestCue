using RestCue.App.UsageEvents;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests.UsageEvents;

public sealed class BackgroundUsageEventWriterTests : IDisposable
{
    [Fact]
    public void Write_preserves_FIFO_ordering()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        using var writer = new BackgroundUsageEventWriter(fake, errors.Add);
        var baseTime = DateTimeOffset.UtcNow;

        writer.Write(UsageEventType.BreakStarted, baseTime);
        writer.Write(UsageEventType.BreakCompleted, baseTime.AddSeconds(1));
        writer.Write(UsageEventType.Paused, baseTime.AddSeconds(2));

        writer.Dispose();

        Assert.Equal(3, fake.Writes.Count);
        Assert.Equal(UsageEventType.BreakStarted, fake.Writes[0].EventType);
        Assert.Equal(UsageEventType.BreakCompleted, fake.Writes[1].EventType);
        Assert.Equal(UsageEventType.Paused, fake.Writes[2].EventType);
    }

    [Fact]
    public void Dispose_drains_buffered_writes()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        var writer = new BackgroundUsageEventWriter(fake, errors.Add);
        var baseTime = DateTimeOffset.UtcNow;

        writer.Write(UsageEventType.IdleStarted, baseTime);
        writer.Write(UsageEventType.IdleEnded, baseTime.AddSeconds(1));

        writer.Dispose();

        Assert.Equal(2, fake.Writes.Count);
        Assert.Empty(errors);
    }

    [Fact]
    public void Write_after_dispose_is_silently_dropped()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        var writer = new BackgroundUsageEventWriter(fake, errors.Add);
        var baseTime = DateTimeOffset.UtcNow;

        writer.Write(UsageEventType.Enabled, baseTime);
        writer.Dispose();

        writer.Write(UsageEventType.Disabled, baseTime.AddSeconds(1));

        Assert.Single(fake.Writes);
        Assert.Equal(UsageEventType.Enabled, fake.Writes[0].EventType);
    }

    [Fact]
    public void Channel_full_does_not_throw_or_crash()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        using var writer = new BackgroundUsageEventWriter(fake, errors.Add, channelCapacity: 1);
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 1000; i++)
            writer.Write(UsageEventType.Paused, baseTime.AddSeconds(i));

        writer.Dispose();

        Assert.True(fake.Writes.Count < 1000, "Some writes should have been dropped due to capacity.");
    }

    [Fact]
    public void Write_failure_invokes_error_callback()
    {
        var fake = new FakeUsageEventRepository(throwOnWrite: true);
        var errors = new List<string>();
        using var writer = new BackgroundUsageEventWriter(fake, errors.Add);
        var baseTime = DateTimeOffset.UtcNow;

        writer.Write(UsageEventType.Disabled, baseTime);
        writer.Dispose();

        Assert.NotEmpty(errors);
        Assert.Contains("failed to persist", errors[0]);
    }

    [Fact]
    public void Dispose_does_not_deadlock_on_a_UI_synchronization_context()
    {
        var originalContext = SynchronizationContext.Current;
        var context = new QueuedSynchronizationContext();
        var disposeReturned = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            using var writer = new BackgroundUsageEventWriter(new FakeUsageEventRepository());
            writer.Dispose();
            disposeReturned.Set();
        })
        {
            IsBackground = true
        };

        try
        {
            thread.Start();
            Assert.True(
                disposeReturned.Wait(TimeSpan.FromSeconds(3)),
                "Dispose deadlocked while waiting for a consumer continuation posted to the UI context.");
        }
        finally
        {
            context.RunPostedCallbacks();
            thread.Join(TimeSpan.FromSeconds(1));
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task Multiple_producers_maintain_FIFO_within_semantics()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        using var writer = new BackgroundUsageEventWriter(fake, errors.Add);
        var baseTime = DateTimeOffset.UtcNow;

        var t1 = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
                writer.Write(UsageEventType.ReminderShown, baseTime.AddMinutes(i));
        });
        var t2 = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
                writer.Write(UsageEventType.BreakCompleted, baseTime.AddMinutes(100 + i));
        });

        await Task.WhenAll(t1, t2);
        writer.Dispose();

        Assert.Equal(100, fake.Writes.Count);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RunExclusiveAsync_runs_between_events_in_queue_order()
    {
        var order = new List<string>();
        var fake = new FakeUsageEventRepository(onWrite: () => order.Add("event"));
        using var writer = new BackgroundUsageEventWriter(fake);

        writer.Write(UsageEventType.BreakStarted, DateTimeOffset.UtcNow);
        int result = await writer.RunExclusiveAsync(async () =>
        {
            order.Add("clear");
            await Task.Yield();
            return 7;
        });
        writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);

        writer.Dispose();

        Assert.Equal(7, result);
        Assert.Equal(["event", "clear", "event"], order);
    }

    [Fact]
    public async Task RunExclusiveAsync_serializes_metadata_write_with_event_writes()
    {
        var order = new List<string>();
        var fake = new FakeUsageEventRepository(onWrite: () => order.Add("event"));
        using var writer = new BackgroundUsageEventWriter(fake);

        writer.Write(UsageEventType.BreakStarted, DateTimeOffset.UtcNow);
        bool recorded = await writer.RunExclusiveAsync(async () =>
        {
            order.Add("clear");
            await Task.Yield();
            order.Add("metadata");
            return true;
        });
        writer.Write(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);

        writer.Dispose();

        Assert.True(recorded);
        Assert.Equal(["event", "clear", "metadata", "event"], order);
    }

    [Fact]
    public async Task Dispose_returns_after_timeout_when_exclusive_operation_is_stuck()
    {
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var writer = new BackgroundUsageEventWriter(new FakeUsageEventRepository());
        var exclusive = writer.RunExclusiveAsync(async () =>
        {
            started.Set();
            release.Wait();
            return 7;
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        var dispose = Task.Run(writer.Dispose);

        Assert.True(await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(3))) == dispose);

        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await exclusive);
    }

    [Fact]
    public void Wire_unwire_re_wire_does_not_duplicate_writes()
    {
        var fake = new FakeUsageEventRepository();
        var errors = new List<string>();
        var evt = new EventSource();

        var writer1 = new BackgroundUsageEventWriter(fake, errors.Add);
        EventHandler h1 = (_, _) => writer1.Write(UsageEventType.ReminderShown, DateTimeOffset.UtcNow);
        evt.Fired += h1;
        evt.Fired -= h1;
        writer1.Dispose();

        var writer2 = new BackgroundUsageEventWriter(fake, errors.Add);
        EventHandler h2 = (_, _) => writer2.Write(UsageEventType.ReminderShown, DateTimeOffset.UtcNow);
        evt.Fired += h2;

        evt.Raise();
        evt.Raise();

        writer2.Dispose();

        Assert.Equal(2, fake.Writes.Count);
        Assert.Equal(UsageEventType.ReminderShown, fake.Writes[0].EventType);
        Assert.Empty(errors);
    }

    public void Dispose()
    {
    }

    private sealed class EventSource
    {
        public event EventHandler? Fired;
        public void Raise() => Fired?.Invoke(this, EventArgs.Empty);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        private readonly object gate = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (gate)
            {
                callbacks.Enqueue((d, state));
            }
        }

        public void RunPostedCallbacks()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) callback;
                lock (gate)
                {
                    if (callbacks.Count == 0)
                    {
                        return;
                    }

                    callback = callbacks.Dequeue();
                }

                callback.Callback(callback.State);
            }
        }
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private TimeSpan elapsed;
        public DateTimeOffset UtcNow => utcNow;

        public TimeSpan Elapsed => elapsed;
        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
            elapsed += duration;
        }
    }

    private sealed class FakeUsageEventRepository : RestCue.Core.UsageEvents.IUsageEventRepository
    {
        private readonly bool throwOnWrite;
        private readonly bool blockWrites;
        private readonly Action? onWrite;
        private readonly ManualResetEventSlim blocker = new(false);
        private readonly object syncRoot = new();

        public List<UsageEvent> Writes { get; } = [];

        public FakeUsageEventRepository(bool throwOnWrite = false, Action? onWrite = null, bool blockWrites = false)
        {
            this.throwOnWrite = throwOnWrite;
            this.onWrite = onWrite;
            this.blockWrites = blockWrites;
        }

        public void StopBlocking() => blocker.Set();

        public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc, UsageEventPayload? payload = null, CancellationToken ct = default)
        {
            if (throwOnWrite)
                throw new InvalidOperationException("Simulated write failure.");

            if (blockWrites)
                blocker.Wait();

            lock (syncRoot)
            {
                Writes.Add(new UsageEvent(0, occurredUtc, eventType, payload));
            }

            onWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
