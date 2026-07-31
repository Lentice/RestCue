using RestCue.App.UsageEvents;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests.UsageEvents;

/// <summary>
/// The first work session after launch was never recorded: the recorder subscribed after
/// the status window had published its opening phase, so it both missed the start and —
/// believing no work was running — swallowed the transition that ended it.
/// </summary>
/// <remarks>
/// These tests assert the property that makes the wiring order irrelevant, rather than
/// asserting one particular order. An order-specific test would have to be kept in step
/// with startup by hand, which is exactly the drift that produced the bug.
/// </remarks>
public sealed class WorkSessionRecorderTests
{
    [Fact]
    public void Attaching_after_work_has_begun_records_the_start_boundary()
    {
        var source = new FakePhaseSource(WorkCyclePhase.Working);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);

        Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
        Assert.True(recorder.IsWorkInProgress);
    }

    [Fact]
    public void Attaching_before_work_begins_records_one_start_boundary_and_no_more()
    {
        var source = new FakePhaseSource(currentPhase: null);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        source.Publish(WorkCyclePhase.Working);

        Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
    }

    /// <summary>
    /// Seeding replays the current phase through the same handler, so being attached early
    /// enough to observe that phase live cannot double-count it.
    /// </summary>
    [Fact]
    public void Seeding_and_observing_the_same_transition_does_not_double_count()
    {
        var source = new FakePhaseSource(WorkCyclePhase.Working);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        source.Publish(WorkCyclePhase.Working);

        Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
    }

    /// <summary>
    /// The defect's second half: the missed start left the recorder believing work was not
    /// running, so the user's first command closed nothing.
    /// </summary>
    [Theory]
    [InlineData(WorkCyclePhase.BreakInProgress)]
    [InlineData(WorkCyclePhase.Paused)]
    [InlineData(WorkCyclePhase.Disabled)]
    [InlineData(WorkCyclePhase.Idle)]
    public void The_first_command_after_launch_closes_the_session_that_was_running(
        WorkCyclePhase endingPhase)
    {
        var source = new FakePhaseSource(WorkCyclePhase.Working);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        source.Publish(endingPhase);

        Assert.Equal(
            [UsageEventType.WorkSessionStarted, UsageEventType.WorkSessionEnded],
            recorded);
        Assert.False(recorder.IsWorkInProgress);
    }

    /// <summary>
    /// Focus Mode counts as continuous work, so entering it must not open a second session
    /// that hides the work leading up to it.
    /// </summary>
    [Fact]
    public void Entering_focus_mode_first_keeps_the_session_that_started_at_launch()
    {
        var source = new FakePhaseSource(WorkCyclePhase.Working);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        source.Publish(WorkCyclePhase.FocusMode);
        source.Publish(WorkCyclePhase.Paused);

        Assert.Equal(
            [UsageEventType.WorkSessionStarted, UsageEventType.WorkSessionEnded],
            recorded);
    }

    /// <summary>
    /// Whatever phase startup happens to attach in, the recorder's belief matches it.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void The_recorders_belief_is_always_derived_from_the_source(WorkCyclePhase phase)
    {
        var source = new FakePhaseSource(phase);
        var recorder = new WorkSessionRecorder(_ => { });

        recorder.Attach(source);

        Assert.Equal(ContinuousWorkPolicy.IsContinuousWork(phase), recorder.IsWorkInProgress);
    }

    [Fact]
    public void Attaching_with_no_work_cycle_yet_records_nothing()
    {
        var source = new FakePhaseSource(currentPhase: null);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);

        Assert.Empty(recorded);
        Assert.False(recorder.IsWorkInProgress);
    }

    [Fact]
    public void Detaching_stops_recording()
    {
        var source = new FakePhaseSource(WorkCyclePhase.Working);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        recorder.Detach();
        source.Publish(WorkCyclePhase.Paused);

        Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
    }

    [Fact]
    public void Attaching_twice_does_not_double_subscribe()
    {
        var source = new FakePhaseSource(WorkCyclePhase.Idle);
        var recorded = new List<UsageEventType>();
        var recorder = new WorkSessionRecorder(recorded.Add);

        recorder.Attach(source);
        recorder.Attach(source);
        source.Publish(WorkCyclePhase.Working);

        Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
    }

    public static TheoryData<WorkCyclePhase> AllPhases()
    {
        var data = new TheoryData<WorkCyclePhase>();
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            data.Add(phase);
        }
        return data;
    }

    private sealed class FakePhaseSource(WorkCyclePhase? currentPhase) : IWorkPhaseSource
    {
        public event EventHandler<WorkCyclePhase>? PhaseChanged;

        public WorkCyclePhase? CurrentPhase { get; private set; } = currentPhase;

        public void Publish(WorkCyclePhase phase)
        {
            CurrentPhase = phase;
            PhaseChanged?.Invoke(this, phase);
        }
    }
}
