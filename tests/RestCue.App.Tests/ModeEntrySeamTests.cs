using RestCue.App;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.App.Tests;

public sealed class ModeEntrySeamTests
{
    [Fact]
    public void ExecutePause_closes_reminder_and_enters_Paused()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int closed = 0;

        App.ExecutePause(tracker, () => closed++);

        Assert.Equal(1, closed);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
    }

    [Fact]
    public void ExecutePause_preserves_Need_and_no_reminder_during_mode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 25; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        var needBefore = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, needBefore);

        int shown = 0;
        tracker.ReminderShown += (_, _) => shown++;

        int closed = 0;
        App.ExecutePause(tracker, () => closed++);

        Assert.Equal(1, closed);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(WorkCyclePhase.Paused, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);
        Assert.Equal(0, shown);
    }

    [Fact]
    public void ExecuteStartFocusMode_closes_reminder_and_enters_FocusMode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int closed = 0;

        App.ExecuteStartFocusMode(tracker, () => closed++);

        Assert.Equal(1, closed);
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
    }

    [Fact]
    public void ExecuteStartFocusMode_preserves_Need_and_no_reminder_during_mode()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock, workInterval: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 25; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            tracker.Tick(TimeSpan.Zero);
        }
        var needBefore = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, needBefore);

        int shown = 0;
        tracker.ReminderShown += (_, _) => shown++;

        int closed = 0;
        App.ExecuteStartFocusMode(tracker, () => closed++);

        Assert.Equal(1, closed);
        Assert.Equal(WorkCyclePhase.FocusMode, tracker.CurrentPhase);
        Assert.Equal(needBefore, tracker.AccumulatedWorkTime);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Tick(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);
        Assert.Equal(needBefore + TimeSpan.FromSeconds(10), tracker.AccumulatedWorkTime);
        Assert.Equal(0, shown);
    }

    private static WorkCycleTracker CreateTracker(
        FakeClock clock,
        TimeSpan? workInterval = null,
        TimeSpan? idleThreshold = null,
        TimeSpan? naturalPause = null)
    {
        return new WorkCycleTracker(
            clock,
            workInterval ?? TimeSpan.FromMinutes(20),
            idleThreshold ?? TimeSpan.FromMinutes(2),
            naturalPause ?? TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
