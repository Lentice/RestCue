using RestCue.Core.Time;

namespace RestCue.Core.Reminders;

public sealed class BreakGuideSession : IBreakGuide
{
    private readonly IClock clock;
    private TimeSpan duration;
    private DateTimeOffset startedUtc;
    private bool completedFired;
    private bool middleFired;
    private TaskCompletionSource<BreakResult>? tcs;

    public BreakGuideSession(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    public BreakGuideSession(IClock clock, TimeSpan duration) : this(clock)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be positive.");
        this.duration = duration;
    }

    public BreakGuidePhase Phase { get; private set; } = BreakGuidePhase.NotStarted;

    public event EventHandler? Completed;
    public event EventHandler? Cancelled;
    public event EventHandler<BreakGuideCue>? CueChanged;

    public void Start()
    {
        if (Phase != BreakGuidePhase.NotStarted)
            return;

        Phase = BreakGuidePhase.Running;
        startedUtc = clock.UtcNow;
        CueChanged?.Invoke(this, BreakGuideCue.Start);
    }

    public async Task<BreakResult> StartAsync(BreakGuideOptions options, CancellationToken cancellationToken = default)
    {
        if (Phase != BreakGuidePhase.NotStarted)
            return BreakResult.Cancelled;

        duration = options.Duration;
        tcs = new TaskCompletionSource<BreakResult>();
        cancellationToken.Register(() => Cancel());

        Start();

        return await tcs.Task;
    }

    public void Tick()
    {
        if (Phase != BreakGuidePhase.Running)
            return;

        var elapsed = clock.UtcNow - startedUtc;

        if (!middleFired && elapsed >= duration / 2)
        {
            middleFired = true;
            CueChanged?.Invoke(this, BreakGuideCue.Middle);
        }

        if (!completedFired && elapsed >= duration)
        {
            completedFired = true;
            Phase = BreakGuidePhase.Completed;
            CueChanged?.Invoke(this, BreakGuideCue.End);
            Completed?.Invoke(this, EventArgs.Empty);
            tcs?.TrySetResult(BreakResult.Completed);
        }
    }

    public void Cancel()
    {
        if (Phase != BreakGuidePhase.Running)
            return;

        Phase = BreakGuidePhase.Cancelled;
        Cancelled?.Invoke(this, EventArgs.Empty);
        tcs?.TrySetResult(BreakResult.Cancelled);
    }
}
