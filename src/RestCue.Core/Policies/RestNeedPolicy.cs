using RestCue.Core.Domain;

namespace RestCue.Core.Policies;

public static class RestNeedPolicy
{
    public static RestDebtLevel Evaluate(
        TimeSpan accumulatedWorkTime,
        TimeSpan workInterval,
        TimeSpan level2Threshold,
        TimeSpan level3Threshold,
        TimeSpan level4Threshold)
    {
        return DebtPolicy.Evaluate(accumulatedWorkTime, workInterval, level2Threshold, level3Threshold, level4Threshold);
    }

    public static TimeSpan Accumulate(TimeSpan accumulatedWorkTime, TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return accumulatedWorkTime;
        return accumulatedWorkTime + delta;
    }
}
