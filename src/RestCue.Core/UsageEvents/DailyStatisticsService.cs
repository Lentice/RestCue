using RestCue.Core.Domain;
using RestCue.Core.Reminders;

namespace RestCue.Core.UsageEvents;

public sealed class DailyStatisticsService : IDailyStatisticsService
{
    private readonly IUsageEventRepository repository;

    public DailyStatisticsService(IUsageEventRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
    }

    public async Task<DailyStatistics> ComputeAsync(
        DateOnly date,
        TimeZoneInfo timezone,
        CancellationToken cancellationToken = default)
    {
        var startOfDayLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(startOfDayLocal, timezone);
        var endOfDayUtc = startOfDayUtc.AddDays(1);

        var lookbackStart = startOfDayUtc.AddDays(-1);

        IReadOnlyList<UsageEvent> preDayEvents;
        IReadOnlyList<UsageEvent> dayEvents;

        try
        {
            preDayEvents = await repository.QueryAsync(lookbackStart, startOfDayUtc, cancellationToken);
            dayEvents = await repository.QueryAsync(startOfDayUtc, endOfDayUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to query usage events: {ex.Message}");
        }

        var hasPartialData = false;

        var preDayState = DeterminePreDayState(preDayEvents);
        bool acc = preDayState.acc;
        int pendingIdleStarts = preDayState.pendingIdleStarts;
        DateTimeOffset? segmentStart = acc ? startOfDayUtc : null;

        List<TimeSpan> completedSegments = [];
        Dictionary<string, TimeSpan> perAppTimes = new(StringComparer.OrdinalIgnoreCase);
        string? currentProcessName = null;
        int idleEndCount = 0;
        int breakCompletedCount = 0;
        int passivePauseCount = 0;
        int snoozedCount = 0;
        int ignoredCount = 0;
        int autoDismissedCount = 0;
        List<DebtLevelChangeEntry> debtHistory = [];
        List<ReminderOutcomeEntry> reminderOutcomes = [];
        Queue<(DateTimeOffset shownAt, long shownEventId)> pendingReminders = new();

        var sortedDayEvents = dayEvents
            .OrderBy(e => e.OccurredUtc)
            .ThenBy(e => e.Id);

        foreach (var ev in sortedDayEvents)
        {
            switch (ev.EventType)
            {
                case UsageEventType.IdleStarted:
                    pendingIdleStarts++;
                    HandleAccStop(ref acc, ref segmentStart, ev.OccurredUtc, completedSegments);
                    break;

                case UsageEventType.IdleEnded:
                    if (pendingIdleStarts > 0)
                    {
                        pendingIdleStarts--;
                        idleEndCount++;
                    }
                    HandleAccStart(ref acc, ref segmentStart, ev.OccurredUtc);
                    break;

                case UsageEventType.Paused:
                    HandleAccStop(ref acc, ref segmentStart, ev.OccurredUtc, completedSegments);
                    break;

                case UsageEventType.Resumed:
                    HandleAccStart(ref acc, ref segmentStart, ev.OccurredUtc);
                    break;

                case UsageEventType.Disabled:
                    HandleAccStop(ref acc, ref segmentStart, ev.OccurredUtc, completedSegments);
                    break;

                case UsageEventType.Enabled:
                    HandleAccStart(ref acc, ref segmentStart, ev.OccurredUtc);
                    break;

                case UsageEventType.BreakStarted:
                    HandleAccStop(ref acc, ref segmentStart, ev.OccurredUtc, completedSegments);
                    TryPairReminder(pendingReminders, reminderOutcomes, ev);
                    break;

                case UsageEventType.BreakCompleted:
                    breakCompletedCount++;
                    HandleAccStart(ref acc, ref segmentStart, ev.OccurredUtc);
                    break;

                case UsageEventType.BreakCancelled:
                    HandleAccStart(ref acc, ref segmentStart, ev.OccurredUtc);
                    break;

                case UsageEventType.PassivePauseDetected:
                    passivePauseCount++;
                    break;

                case UsageEventType.ReminderShown:
                    pendingReminders.Enqueue((ev.OccurredUtc, ev.Id));
                    break;

                case UsageEventType.ReminderDismissed:
                    var result = TryGetReminderResult(ev, ref hasPartialData);
                    if (result == ReminderResult.Snoozed) snoozedCount++;
                    else if (result == ReminderResult.Ignored) ignoredCount++;
                    else if (result == ReminderResult.AutoDismissed) autoDismissedCount++;

                    TryPairReminder(pendingReminders, reminderOutcomes, ev, result);
                    break;

                case UsageEventType.RestDebtLevelChanged:
                    var debtPayload = TryGetDebtPayload(ev, ref hasPartialData);
                    if (debtPayload is not null)
                    {
                        debtHistory.Add(new DebtLevelChangeEntry(
                            ev.OccurredUtc, ev.Id,
                            debtPayload.Previous, debtPayload.Current));
                    }
                    break;

                case UsageEventType.FocusModeStarted:
                case UsageEventType.FocusModeEnded:
                case UsageEventType.CooldownStarted:
                case UsageEventType.CooldownEnded:
                    break;

                case UsageEventType.ForegroundProcessChanged:
                    var procName = TryGetProcessName(ev, ref hasPartialData);
                    if (procName is not null && acc && segmentStart.HasValue)
                    {
                        var segDuration = ev.OccurredUtc - segmentStart.Value;
                        if (segDuration > TimeSpan.Zero)
                        {
                            completedSegments.Add(segDuration);
                            if (currentProcessName is not null)
                                AddPerAppTime(perAppTimes, currentProcessName, segDuration);
                        }
                        segmentStart = ev.OccurredUtc;
                    }
                    currentProcessName = procName;
                    break;
            }
        }

        // F-05: Drain unpaired reminders (未配對保留為未完成)
        while (pendingReminders.Count > 0)
        {
            var (shownAt, shownId) = pendingReminders.Dequeue();
            reminderOutcomes.Add(new ReminderOutcomeEntry(
                shownAt, shownId, null, null));
        }

        if (acc && segmentStart.HasValue)
        {
            var finalDuration = endOfDayUtc - segmentStart.Value;
            if (finalDuration > TimeSpan.Zero)
            {
                completedSegments.Add(finalDuration);
                if (currentProcessName is not null)
                    AddPerAppTime(perAppTimes, currentProcessName, finalDuration);
            }
        }

        var totalWork = completedSegments.Count > 0
            ? completedSegments.Aggregate(TimeSpan.Zero, (a, b) => a + b)
            : TimeSpan.Zero;
        var longest = completedSegments.Count > 0
            ? completedSegments.Max()
            : TimeSpan.Zero;

        var endedSegments = completedSegments
            .Take(completedSegments.Count - (acc ? 1 : 0))
            .ToList();

        TimeSpan? average = endedSegments.Count > 0
            ? TimeSpan.FromTicks((long)endedSegments.Average(s => s.Ticks))
            : null;

        var status = hasPartialData ? DailyStatisticsStatus.PartialData : DailyStatisticsStatus.Success;

        return new DailyStatistics(
            Status: status,
            ErrorMessage: null,
            EffectiveWorkTime: totalWork,
            BreakCompletedCount: breakCompletedCount,
            IdleResetCount: idleEndCount,
            PassivePauseDetectedCount: passivePauseCount,
            SnoozedCount: snoozedCount,
            IgnoredCount: ignoredCount,
            AutoDismissedCount: autoDismissedCount,
            LongestContinuousWork: longest,
            AverageWorkCycleDuration: average,
            DebtLevelHistory: debtHistory.AsReadOnly(),
            ReminderOutcomes: reminderOutcomes.AsReadOnly(),
            PerAppWorkTime: perAppTimes);
    }

    private static (bool acc, int pendingIdleStarts) DeterminePreDayState(
        IReadOnlyList<UsageEvent> preDayEvents)
    {
        bool acc = false;
        int pendingIdleStarts = 0;

        var sorted = preDayEvents
            .OrderBy(e => e.OccurredUtc)
            .ThenBy(e => e.Id);

        foreach (var ev in sorted)
        {
            switch (ev.EventType)
            {
                case UsageEventType.IdleStarted:
                    pendingIdleStarts++;
                    acc = false;
                    break;
                case UsageEventType.IdleEnded:
                    if (pendingIdleStarts > 0) pendingIdleStarts--;
                    acc = true;
                    break;
                case UsageEventType.Paused:
                    acc = false;
                    break;
                case UsageEventType.Resumed:
                    acc = true;
                    break;
                case UsageEventType.Disabled:
                    acc = false;
                    break;
                case UsageEventType.Enabled:
                    acc = true;
                    break;
                case UsageEventType.BreakStarted:
                    acc = false;
                    break;
                case UsageEventType.BreakCompleted:
                    acc = true;
                    break;
                case UsageEventType.BreakCancelled:
                    acc = true;
                    break;
            }
        }

        return (acc, pendingIdleStarts);
    }

    private static void HandleAccStop(
        ref bool acc, ref DateTimeOffset? segmentStart,
        DateTimeOffset eventTime, List<TimeSpan> completedSegments)
    {
        if (acc && segmentStart.HasValue)
        {
            var duration = eventTime - segmentStart.Value;
            if (duration > TimeSpan.Zero)
                completedSegments.Add(duration);
        }
        acc = false;
        segmentStart = null;
    }

    private static void HandleAccStart(
        ref bool acc, ref DateTimeOffset? segmentStart,
        DateTimeOffset eventTime)
    {
        acc = true;
        if (!segmentStart.HasValue)
            segmentStart = eventTime;
    }

    private static ReminderResult? TryGetReminderResult(UsageEvent ev, ref bool hasPartialData)
    {
        if (ev.Payload is ReminderDismissedPayload p)
            return p.Result;

        hasPartialData = true;
        return null;
    }

    private static string? TryGetProcessName(UsageEvent ev, ref bool hasPartialData)
    {
        if (ev.Payload is ForegroundProcessChangedPayload p)
            return p.ProcessName;

        hasPartialData = true;
        return null;
    }

    private static void AddPerAppTime(Dictionary<string, TimeSpan> perApp, string processName, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        if (perApp.TryGetValue(processName, out var existing))
            perApp[processName] = existing + duration;
        else
            perApp[processName] = duration;
    }

    private static RestDebtLevelChangedPayload? TryGetDebtPayload(UsageEvent ev, ref bool hasPartialData)
    {
        if (ev.Payload is RestDebtLevelChangedPayload p)
            return p;

        hasPartialData = true;
        return null;
    }

    private static void TryPairReminder(
        Queue<(DateTimeOffset shownAt, long shownEventId)> pendingReminders,
        List<ReminderOutcomeEntry> outcomes,
        UsageEvent ev,
        ReminderResult? dismissalResult = null)
    {
        if (pendingReminders.Count == 0)
            return;

        var (shownAt, shownId) = pendingReminders.Dequeue();

        ReminderOutcome? outcome;
        DateTimeOffset? outcomeAt;

        if (dismissalResult.HasValue)
        {
            outcome = dismissalResult.Value switch
            {
                Reminders.ReminderResult.Snoozed => ReminderOutcome.Snoozed,
                Reminders.ReminderResult.Ignored => ReminderOutcome.Ignored,
                Reminders.ReminderResult.AutoDismissed => ReminderOutcome.AutoDismissed,
                _ => null
            };
            outcomeAt = ev.OccurredUtc;
        }
        else if (ev.EventType == UsageEventType.BreakStarted)
        {
            outcome = ReminderOutcome.BreakStarted;
            outcomeAt = ev.OccurredUtc;
        }
        else
        {
            outcome = null;
            outcomeAt = null;
        }

        outcomes.Add(new ReminderOutcomeEntry(
            shownAt, shownId, outcome, outcomeAt));
    }

    private static DailyStatistics Failure(string message)
    {
        return new DailyStatistics(
            DailyStatisticsStatus.Failure, message,
            TimeSpan.Zero, 0, 0, 0, 0, 0, 0,
            TimeSpan.Zero, null, [], [],
            new Dictionary<string, TimeSpan>());
    }
}
