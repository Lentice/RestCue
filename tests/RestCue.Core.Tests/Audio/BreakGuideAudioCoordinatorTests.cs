using RestCue.Core.Audio;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests.Audio;

public sealed class BreakGuideAudioCoordinatorTests
{
    [Fact]
    public void Default_mode_is_chime()
    {
        var coordinator = new BreakGuideAudioCoordinator(new FakeAudioPlayer());
        Assert.Equal(BreakGuideMode.Chime, coordinator.CurrentMode);
    }

    [Fact]
    public void Constructor_throws_on_null_player()
    {
        Assert.Throws<ArgumentNullException>(() => new BreakGuideAudioCoordinator(null!));
    }

    [Fact]
    public void Successful_guide_plays_all_three_cues()
    {
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);
        coordinator.HandleCue(BreakGuideCue.Middle);
        coordinator.HandleCue(BreakGuideCue.End);

        Assert.Equal([BreakGuideCue.Start, BreakGuideCue.Middle, BreakGuideCue.End], player.Played);
        Assert.False(coordinator.IsDegraded);
    }

    [Fact]
    public void Initialization_failure_degrades_to_visual_only()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);
        AudioFailureReason? degradedReason = null;
        int degradedCount = 0;
        coordinator.DegradedToVisual += (_, r) => { degradedReason = r; degradedCount++; };

        coordinator.BeginGuide(true);

        Assert.Equal(BreakGuideMode.VisualOnly, coordinator.CurrentMode);
        Assert.Equal(1, degradedCount);
        Assert.Equal(AudioFailureReason.InitializationFailed, degradedReason);
        Assert.Empty(player.Played);
    }

    [Fact]
    public void Mid_guide_playback_failure_degrades_and_stops()
    {
        var player = new FakeAudioPlayer { FailOnPlayCall = 2 };
        var coordinator = new BreakGuideAudioCoordinator(player);
        int degradedCount = 0;
        coordinator.DegradedToVisual += (_, _) => degradedCount++;

        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);
        coordinator.HandleCue(BreakGuideCue.Middle);

        Assert.Equal([BreakGuideCue.Start], player.Played);
        Assert.True(coordinator.IsDegraded);
        Assert.Equal(1, degradedCount);
        Assert.Equal(1, player.StopCount);
    }

    [Fact]
    public void Degradation_event_fires_only_once()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);
        int degradedCount = 0;
        coordinator.DegradedToVisual += (_, _) => degradedCount++;

        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);
        coordinator.HandleCue(BreakGuideCue.Middle);
        coordinator.HandleCue(BreakGuideCue.End);

        Assert.Equal(1, degradedCount);
    }

    [Fact]
    public void Device_unavailable_is_silent()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);

        var ex = Record.Exception(() => coordinator.BeginGuide(true));
        Assert.Null(ex);
    }

    [Fact]
    public void Audio_not_allowed_starts_visual_only_without_degradation()
    {
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);
        int degradedCount = 0;
        coordinator.DegradedToVisual += (_, _) => degradedCount++;

        coordinator.BeginGuide(false);

        Assert.Equal(BreakGuideMode.VisualOnly, coordinator.CurrentMode);
        Assert.Equal(0, degradedCount);
        Assert.Empty(player.Played);
    }

    [Fact]
    public void EndGuide_stops_player()
    {
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(true);
        coordinator.EndGuide();

        Assert.Equal(1, player.StopCount);
    }

    [Fact]
    public void EndGuide_resets_mode_to_initial()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(true);
        Assert.Equal(BreakGuideMode.VisualOnly, coordinator.CurrentMode);

        coordinator.EndGuide();

        Assert.Equal(BreakGuideMode.Chime, coordinator.CurrentMode);
    }

    [Fact]
    public void EndGuide_resets_degraded_flag()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(true);
        Assert.True(coordinator.IsDegraded);

        coordinator.EndGuide();

        Assert.False(coordinator.IsDegraded);
    }

    [Fact]
    public void Stop_failure_is_swallowed()
    {
        var player = new FakeAudioPlayer { ThrowOnStop = true };
        var coordinator = new BreakGuideAudioCoordinator(player);

        var ex = Record.Exception(() => coordinator.EndGuide());
        Assert.Null(ex);
    }

    [Fact]
    public void Stop_after_begin_failure_is_harmless()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);
        coordinator.BeginGuide(true);

        var ex = Record.Exception(() => coordinator.EndGuide());
        Assert.Null(ex);
    }

    [Fact]
    public void Visual_only_after_begin_delivers_no_audio()
    {
        var player = new FakeAudioPlayer();
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(false);
        coordinator.HandleCue(BreakGuideCue.Start);
        Assert.Empty(player.Played);
    }

    [Fact]
    public void Visual_only_after_degradation_delivers_no_audio()
    {
        var player = new FakeAudioPlayer { FailInitialize = true };
        var coordinator = new BreakGuideAudioCoordinator(player);

        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Middle);
        coordinator.HandleCue(BreakGuideCue.End);

        Assert.Empty(player.Played);
    }

    [Fact]
    public void Multiple_cues_after_degradation_do_not_repeat_degraded_event()
    {
        var player = new FakeAudioPlayer { FailOnPlayCall = 1 };
        var coordinator = new BreakGuideAudioCoordinator(player);
        int degradedCount = 0;
        coordinator.DegradedToVisual += (_, _) => degradedCount++;

        coordinator.BeginGuide(true);
        coordinator.HandleCue(BreakGuideCue.Start);
        coordinator.HandleCue(BreakGuideCue.Middle);
        coordinator.HandleCue(BreakGuideCue.End);

        Assert.Equal(1, degradedCount);
    }

    private sealed class FakeAudioPlayer : IBreakGuideAudioPlayer
    {
        public bool FailInitialize { get; set; }
        public int FailOnPlayCall { get; set; } = int.MaxValue;
        public bool ThrowOnStop { get; set; }
        public bool ThrowOnDispose { get; set; }
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

        public void Stop()
        {
            StopCount++;
            if (ThrowOnStop)
                throw new InvalidOperationException("Stop failed");
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
                throw new InvalidOperationException("Dispose failed");
        }
    }
}
