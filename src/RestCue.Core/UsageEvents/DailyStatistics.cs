namespace RestCue.Core.UsageEvents;

using Domain;

public enum DailyStatisticsStatus
{
    Success,
    PartialData,
    Failure
}

public sealed record DailyStatistics(
    DailyStatisticsStatus Status,
    string? ErrorMessage,
    TimeSpan EffectiveWorkTime,
    int BreakCompletedCount,
    int IdleResetCount,
    int PassivePauseDetectedCount,
    int SnoozedCount,
    int IgnoredCount,
    int AutoDismissedCount,
    TimeSpan LongestContinuousWork,
    TimeSpan? AverageWorkCycleDuration,
    IReadOnlyList<DebtLevelChangeEntry> DebtLevelHistory,
    IReadOnlyList<ReminderOutcomeEntry> ReminderOutcomes);

public sealed record DebtLevelChangeEntry(
    DateTimeOffset OccurredUtc,
    long EventId,
    RestDebtLevel Previous,
    RestDebtLevel Current);

public sealed record ReminderOutcomeEntry(
    DateTimeOffset ReminderShownAt,
    long ReminderShownEventId,
    ReminderOutcome? Outcome,
    DateTimeOffset? OutcomeAt);

public enum ReminderOutcome
{
    Snoozed,
    Ignored,
    AutoDismissed,
    BreakStarted
}
