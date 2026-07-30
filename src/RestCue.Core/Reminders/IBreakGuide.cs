namespace RestCue.Core.Reminders;

public sealed record BreakGuideOptions(TimeSpan Duration, bool ReduceMotion);

public enum BreakResult
{
    Completed,
    Cancelled,
}

public interface IBreakGuide
{
    event EventHandler<BreakGuideCue>? CueChanged;

    Task<BreakResult> StartAsync(BreakGuideOptions options, CancellationToken cancellationToken = default);
}
