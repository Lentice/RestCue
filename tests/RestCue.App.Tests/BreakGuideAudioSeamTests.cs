using RestCue.Core.Audio;
using RestCue.Core.Reminders;
using RestCue.Core.Time;
using RestCue.Infrastructure.Audio;
using Xunit;

namespace RestCue.App.Tests;

public sealed class BreakGuideAudioSeamTests
{
    [Fact]
    public void Degradation_does_not_change_break_duration()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        bool completed = false;
        tracker.BreakCompleted += (_, _) => completed = true;

        tracker.ManualStartBreak();
        var player = new FakeAudioPlayer { FailOnPlayCall = 1 };
        var coordinator = new BreakGuideAudioCoordinator(player);
        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.True(completed);
    }

    [Fact]
    public void Degradation_does_not_emit_break_cancelled()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        bool cancelled = false;
        tracker.BreakCancelled += (_, _) => cancelled = true;

        tracker.ManualStartBreak();
        var player = new FakeAudioPlayer { FailOnPlayCall = 1 };
        var coordinator = new BreakGuideAudioCoordinator(player);
        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.Tick(TimeSpan.Zero);

        Assert.False(cancelled);
    }

    [Fact]
    public void Cancel_during_audio_still_emits_single_break_cancelled()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        int cancelled = 0;
        tracker.BreakCancelled += (_, _) => cancelled++;

        tracker.ManualStartBreak();
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);
        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);

        tracker.CancelBreak();
        coordinator.EndGuide();

        Assert.Equal(1, cancelled);
        Assert.Equal(1, player.StopCount);
    }

    [Fact]
    public void Speech_text_contains_no_digits()
    {
        foreach (BreakGuideCue cue in Enum.GetValues<BreakGuideCue>())
        {
            Assert.DoesNotContain(WindowsBreakGuideAudioPlayer.GetSpeechText(cue), c => char.IsDigit(c));
        }
    }

    [Fact]
    public void Visual_only_maintains_completion_semantics()
    {
        var clock = new FakeClock();
        var tracker = CreateTracker(clock);
        bool completed = false;
        tracker.BreakCompleted += (_, _) => completed = true;

        tracker.ManualStartBreak();
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);
        coordinator.BeginGuide(false);

        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.Tick(TimeSpan.Zero);

        Assert.True(completed);
        Assert.Empty(player.Played);
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset _utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private static WorkCycleTracker CreateTracker(FakeClock clock)
    {
        return new WorkCycleTracker(
            clock,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));
    }

    private sealed class FakeAudioPlayer : IBreakGuideAudioPlayer
    {
        public bool FailInitialize { get; set; }
        public int FailOnPlayCall { get; set; } = int.MaxValue;
        public List<BreakGuideCue> Played { get; } = [];
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        private int playCallCount;

        public bool TryInitialize(out AudioFailureReason? failure)
        {
            if (FailInitialize)
            {
                failure = AudioFailureReason.InitializationFailed;
                return false;
            }
            failure = null;
            return true;
        }

        public bool TryPlay(BreakGuideCue cue, BreakGuideMode mode, out AudioFailureReason? failure)
        {
            playCallCount++;
            if (playCallCount >= FailOnPlayCall)
            {
                failure = AudioFailureReason.PlaybackFailed;
                return false;
            }
            Played.Add(cue);
            failure = null;
            return true;
        }

        public void Stop() => StopCount++;
        public void Dispose() => DisposeCount++;
    }
}
