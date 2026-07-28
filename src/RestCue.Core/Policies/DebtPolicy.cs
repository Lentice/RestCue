using RestCue.Core.Domain;

namespace RestCue.Core.Policies;

public static class DebtPolicy
{
    public static RestDebtLevel Evaluate(
        TimeSpan accumulatedWorkTime,
        TimeSpan level1,
        TimeSpan level2,
        TimeSpan level3,
        TimeSpan level4)
    {
        if (accumulatedWorkTime >= level4) return RestDebtLevel.Level4;
        if (accumulatedWorkTime >= level3) return RestDebtLevel.Level3;
        if (accumulatedWorkTime >= level2) return RestDebtLevel.Level2;
        if (accumulatedWorkTime >= level1) return RestDebtLevel.Level1;
        return RestDebtLevel.Level0;
    }

    public static TimeSpan? GetNextThreshold(
        RestDebtLevel current,
        TimeSpan level1,
        TimeSpan level2,
        TimeSpan level3,
        TimeSpan level4)
    {
        return current switch
        {
            RestDebtLevel.Level0 => level1,
            RestDebtLevel.Level1 => level2,
            RestDebtLevel.Level2 => level3,
            RestDebtLevel.Level3 => level4,
            RestDebtLevel.Level4 => null,
            _ => null
        };
    }

    public static void ValidateThresholds(
        TimeSpan level1,
        TimeSpan level2,
        TimeSpan level3,
        TimeSpan level4)
    {
        if (level1 <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(level1), level1, "Level 1 threshold must be positive.");

        if (level2 <= level1)
            throw new ArgumentOutOfRangeException(
                nameof(level2), level2, "Level 2 threshold must be greater than Level 1.");

        if (level3 <= level2)
            throw new ArgumentOutOfRangeException(
                nameof(level3), level3, "Level 3 threshold must be greater than Level 2.");

        if (level4 <= level3)
            throw new ArgumentOutOfRangeException(
                nameof(level4), level4, "Level 4 threshold must be greater than Level 3.");
    }
}
