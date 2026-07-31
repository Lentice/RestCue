using RestCue.Core.Reminders;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.Core.Tests.Reminders;

public sealed class BreakGuideSessionTests
{
    [Fact]
    public void Start_enters_Running_and_emits_start_cue()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int cueCount = 0;
        BreakGuideCue receivedCue = default;
        session.CueChanged += (_, cue) => { cueCount++; receivedCue = cue; };

        session.Start();

        Assert.Equal(BreakGuidePhase.Running, session.Phase);
        Assert.Equal(1, cueCount);
        Assert.Equal(BreakGuideCue.Start, receivedCue);
    }

    [Fact]
    public void Tick_before_duration_does_not_complete()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int completed = 0;
        session.Completed += (_, _) => completed++;

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(19));
        session.Tick();

        Assert.Equal(0, completed);
        Assert.Equal(BreakGuidePhase.Running, session.Phase);
    }

    [Fact]
    public void Tick_at_exact_duration_completes_once()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int completed = 0;
        session.Completed += (_, _) => completed++;

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(20));
        session.Tick();
        session.Tick();

        Assert.Equal(1, completed);
        Assert.Equal(BreakGuidePhase.Completed, session.Phase);
    }

    [Fact]
    public void Tick_after_duration_does_not_recomplete()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int completed = 0;
        session.Completed += (_, _) => completed++;

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(20));
        session.Tick();
        clock.Advance(TimeSpan.FromSeconds(10));
        session.Tick();

        Assert.Equal(1, completed);
    }

    [Fact]
    public void Middle_cue_emitted_once_at_half_duration()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int middleCount = 0;
        session.CueChanged += (_, cue) => { if (cue == BreakGuideCue.Middle) middleCount++; };

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        session.Tick();
        session.Tick();
        session.Tick();

        Assert.Equal(1, middleCount);
    }

    [Fact]
    public void Cancel_before_duration_emits_cancelled_once()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int cancelled = 0;
        int completed = 0;
        session.Cancelled += (_, _) => cancelled++;
        session.Completed += (_, _) => completed++;

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(5));
        session.Cancel();
        session.Cancel();

        Assert.Equal(1, cancelled);
        Assert.Equal(0, completed);
        Assert.Equal(BreakGuidePhase.Cancelled, session.Phase);
    }

    [Fact]
    public void Cancel_after_completion_is_noop()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int cancelled = 0;
        session.Cancelled += (_, _) => cancelled++;

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(20));
        session.Tick();
        session.Cancel();

        Assert.Equal(0, cancelled);
        Assert.Equal(BreakGuidePhase.Completed, session.Phase);
    }

    [Fact]
    public void Start_is_idempotent()
    {
        var clock = new FakeClock();
        var session = new BreakGuideSession(clock, TimeSpan.FromSeconds(20));
        int cueCount = 0;
        session.CueChanged += (_, _) => cueCount++;

        session.Start();
        var phaseAfterFirst = session.Phase;
        var firstStartTime = clock.UtcNow;

        clock.Advance(TimeSpan.FromSeconds(5));
        session.Start();

        Assert.Equal(1, cueCount);
        Assert.Equal(BreakGuidePhase.Running, phaseAfterFirst);
        Assert.Equal(BreakGuidePhase.Running, session.Phase);
    }

    [Fact]
    public void Text_for_all_cues_contains_no_digits()
    {
        foreach (BreakGuideCue cue in Enum.GetValues<BreakGuideCue>())
        {
            string text = BreakGuideText.ForCue(cue);
            Assert.DoesNotContain(text, c => char.IsDigit(c));
        }
    }

    [Fact]
    public void Constructor_throws_for_non_positive_duration()
    {
        var clock = new FakeClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakGuideSession(clock, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakGuideSession(clock, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_throws_for_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() => new BreakGuideSession(null!, TimeSpan.FromSeconds(20)));
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
