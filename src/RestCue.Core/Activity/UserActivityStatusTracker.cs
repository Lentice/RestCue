namespace RestCue.Core.Activity;

public sealed class UserActivityStatusTracker
{
    private readonly IUserActivityMonitor activityMonitor;
    private readonly UserActivityStatusEvaluator evaluator;

    public UserActivityStatusTracker(
        IUserActivityMonitor activityMonitor,
        UserActivityStatusEvaluator evaluator)
    {
        this.activityMonitor = activityMonitor;
        this.evaluator = evaluator;
    }

    public UserActivityStatus CurrentStatus { get; private set; } = UserActivityStatus.Idle;

    public UserActivityStatus Refresh()
    {
        CurrentStatus = evaluator.Evaluate(activityMonitor.GetCurrentActivity());
        return CurrentStatus;
    }
}
