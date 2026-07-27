namespace RestCue.Core.Activity;

public sealed class UserActivityStatusEvaluator
{
    private readonly TimeSpan idleThreshold;

    public UserActivityStatusEvaluator(TimeSpan idleThreshold)
    {
        if (idleThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleThreshold),
                idleThreshold,
                "Idle threshold must be positive.");
        }

        this.idleThreshold = idleThreshold;
    }

    public UserActivityStatus Evaluate(UserActivitySample sample)
    {
        if (!sample.IsAvailable)
        {
            // Unknown activity must not be counted as effective work.
            return UserActivityStatus.Idle;
        }

        return sample.IdleDuration >= idleThreshold
            ? UserActivityStatus.Idle
            : UserActivityStatus.Working;
    }
}
