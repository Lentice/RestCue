using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests.UsageEvents;

/// <summary>
/// Quitting during a break used to leave a break-started event with no matching outcome:
/// shutdown stopped the timers but never left the break phase. Everything that pairs break
/// events — completion rate, outcome counts, total rest time — was skewed by every session
/// that ended that way.
/// </summary>
/// <remarks>
/// These assert the <em>order</em> of the shutdown steps, not merely that they all ran.
/// Order is the defect: cancelling after persistence is unwired writes nothing, and
/// cancelling after the application-stopped event puts the log out of sequence.
/// </remarks>
public sealed class ShutdownBreakOutcomeTests
{
    [Fact]
    public void The_break_is_ended_before_the_app_stopped_event_and_before_recording_is_released()
    {
        var order = new List<string>();

        App.ExecuteShutdownSequence(
            endInProgressBreak: () => order.Add("break-ended"),
            writeAppStopped: () => order.Add("app-stopped"),
            releaseRecordingAndResources: () => order.Add("released"),
            logError: _ => { });

        Assert.Equal(["break-ended", "app-stopped", "released"], order);
    }

    [Fact]
    public void A_failure_ending_the_break_still_writes_the_app_stopped_event_and_releases()
    {
        var order = new List<string>();
        var logged = new List<string>();

        App.ExecuteShutdownSequence(
            endInProgressBreak: () => throw new InvalidOperationException("boom"),
            writeAppStopped: () => order.Add("app-stopped"),
            releaseRecordingAndResources: () => order.Add("released"),
            logError: logged.Add);

        Assert.Equal(["app-stopped", "released"], order);
        Assert.Contains(logged, m => m.Contains("boom", StringComparison.Ordinal));
    }

    /// <summary>
    /// End to end over a real tracker with the usage-event handlers attached the way
    /// startup attaches them, so the assertion is about the recorded event sequence rather
    /// than about which delegates were invoked.
    /// </summary>
    [Fact]
    public void Quitting_mid_break_records_a_cancellation_then_the_app_stopped_event()
    {
        var clock = new FakeClock();
        WorkCycleTracker tracker = StartedBreak(clock);
        Assert.Equal(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);

        var recorded = new List<UsageEventType>();
        bool persistenceWired = true;

        // Mirrors the real writer: once persistence is unwired, nothing more is stored.
        // A cancel that happened after the release step would therefore record nothing,
        // which is exactly the failure mode being guarded against.
        tracker.BreakCancelled += (_, _) =>
        {
            if (persistenceWired) recorded.Add(UsageEventType.BreakCancelled);
        };

        App.ExecuteShutdownSequence(
            endInProgressBreak: tracker.CancelBreak,
            writeAppStopped: () =>
            {
                if (persistenceWired) recorded.Add(UsageEventType.AppStopped);
            },
            releaseRecordingAndResources: () => persistenceWired = false,
            logError: _ => { });

        Assert.Equal([UsageEventType.BreakCancelled, UsageEventType.AppStopped], recorded);
        Assert.NotEqual(WorkCyclePhase.BreakInProgress, tracker.CurrentPhase);
    }

    [Theory]
    [InlineData(WorkCyclePhase.Working)]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.Disabled)]
    public void Quitting_outside_a_break_records_no_cancellation(WorkCyclePhase phase)
    {
        var clock = new FakeClock();
        WorkCycleTracker tracker = TrackerIn(phase, clock);

        var recorded = new List<UsageEventType>();
        tracker.BreakCancelled += (_, _) => recorded.Add(UsageEventType.BreakCancelled);

        App.ExecuteShutdownSequence(
            endInProgressBreak: tracker.CancelBreak,
            writeAppStopped: () => recorded.Add(UsageEventType.AppStopped),
            releaseRecordingAndResources: () => { },
            logError: _ => { });

        Assert.Equal([UsageEventType.AppStopped], recorded);
    }

    /// <summary>
    /// Cancelling twice — once directly and once through the reminder-window closing path,
    /// as <c>EndBreakForShutdown</c> does — must still produce exactly one outcome.
    /// </summary>
    [Fact]
    public void Ending_the_break_twice_records_one_cancellation()
    {
        var clock = new FakeClock();
        WorkCycleTracker tracker = StartedBreak(clock);

        int cancellations = 0;
        tracker.BreakCancelled += (_, _) => cancellations++;

        tracker.CancelBreak();
        tracker.CancelBreak();

        Assert.Equal(1, cancellations);
    }

    private static WorkCycleTracker StartedBreak(FakeClock clock)
    {
        WorkCycleTracker tracker = TrackerIn(WorkCyclePhase.Working, clock);

        tracker.ManualStartBreak();
        return tracker;
    }

    private static WorkCycleTracker TrackerIn(WorkCyclePhase phase, FakeClock clock)
    {
        var tracker = WorkCycleTrackerFactory.Create(AppSettings.Default, clock);
        tracker.Tick(TimeSpan.Zero);

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                tracker.Pause();
                break;
            case WorkCyclePhase.Disabled:
                tracker.Disable();
                break;
        }

        return tracker;
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
}
