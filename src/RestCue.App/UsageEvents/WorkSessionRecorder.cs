using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;

namespace RestCue.App.UsageEvents;

/// <summary>
/// A phase source the work-session recorder can both listen to and interrogate.
/// </summary>
/// <remarks>
/// <see cref="CurrentPhase"/> is what makes the recorder independent of when it is
/// attached: the recorder never has to assume what it missed, it can ask.
/// </remarks>
internal interface IWorkPhaseSource
{
    event EventHandler<WorkCyclePhase>? PhaseChanged;

    /// <summary>The phase right now, or <c>null</c> if no work cycle is running yet.</summary>
    WorkCyclePhase? CurrentPhase { get; }
}

/// <summary>
/// Turns work-cycle phase transitions into the paired start and end boundaries that the
/// daily statistics add up.
/// </summary>
/// <remarks>
/// The recorder used to live inline in application startup, where it subscribed after the
/// status window had already published its opening phase. It therefore missed the "work has
/// begun" announcement and, because its state defaulted to "not working", it also swallowed
/// the transition that ended that first stretch — the whole session before the user's first
/// command was absent from the statistics.
/// <para>
/// The fix is not a different subscription order but the removal of the dependency on one:
/// <see cref="Attach"/> seeds itself from <see cref="IWorkPhaseSource.CurrentPhase"/>, so the
/// recorder's belief is derived from the tracker rather than assumed. Because transitions are
/// edge-triggered, replaying the current phase through the same handler is idempotent, so
/// attaching before the opening phase is published produces exactly one start boundary too.
/// </para>
/// </remarks>
internal sealed class WorkSessionRecorder(Action<UsageEventType> write)
{
    private readonly Action<UsageEventType> write =
        write ?? throw new ArgumentNullException(nameof(write));

    private IWorkPhaseSource? source;

    /// <summary>Whether the recorder currently believes a work session is running.</summary>
    internal bool IsWorkInProgress { get; private set; }

    internal void Attach(IWorkPhaseSource phaseSource)
    {
        ArgumentNullException.ThrowIfNull(phaseSource);
        if (source != null) return;

        source = phaseSource;
        phaseSource.PhaseChanged += OnPhaseChanged;

        // Seed from the tracker rather than from the default. A phase that has already been
        // published is not a phase that never happened.
        if (phaseSource.CurrentPhase is WorkCyclePhase phase)
        {
            OnPhaseChanged(phaseSource, phase);
        }
    }

    internal void Detach()
    {
        if (source == null) return;

        source.PhaseChanged -= OnPhaseChanged;
        source = null;
    }

    private void OnPhaseChanged(object? sender, WorkCyclePhase newPhase)
    {
        bool isWorking = ContinuousWorkPolicy.IsContinuousWork(newPhase);
        if (isWorking == IsWorkInProgress) return;

        write(isWorking ? UsageEventType.WorkSessionStarted : UsageEventType.WorkSessionEnded);
        IsWorkInProgress = isWorking;
    }
}
