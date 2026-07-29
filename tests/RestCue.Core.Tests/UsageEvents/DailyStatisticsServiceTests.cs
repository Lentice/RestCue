using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.Core.Tests.UsageEvents;

public sealed class DailyStatisticsServiceTests
{
    [Fact]
    public async Task ComputeAsync_empty_day_returns_all_zeros()
    {
        var repo = new FakeUsageEventRepository();
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Success, result.Status);
        Assert.Equal(TimeSpan.Zero, result.EffectiveWorkTime);
        Assert.Equal(0, result.BreakCompletedCount);
        Assert.Equal(0, result.IdleResetCount);
        Assert.Equal(0, result.PassivePauseDetectedCount);
        Assert.Equal(0, result.SnoozedCount);
        Assert.Equal(0, result.IgnoredCount);
        Assert.Equal(0, result.AutoDismissedCount);
        Assert.Equal(TimeSpan.Zero, result.LongestContinuousWork);
        Assert.Null(result.AverageWorkCycleDuration);
        Assert.Empty(result.DebtLevelHistory);
        Assert.Empty(result.ReminderOutcomes);
    }

    [Fact]
    public async Task ComputeAsync_counts_BreakCompleted()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.BreakCompleted, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(5), UsageEventType.BreakCompleted, null));
        repo.Events.Add(new UsageEvent(3, baseTime.AddMinutes(10), UsageEventType.BreakCompleted, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Success, result.Status);
        Assert.Equal(3, result.BreakCompletedCount);
    }

    [Fact]
    public async Task ComputeAsync_counts_idle_resets_from_complete_cycles()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(5), UsageEventType.IdleEnded, null));
        repo.Events.Add(new UsageEvent(3, baseTime.AddMinutes(10), UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(4, baseTime.AddMinutes(15), UsageEventType.IdleEnded, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Success, result.Status);
        Assert.Equal(2, result.IdleResetCount);
    }

    [Fact]
    public async Task ComputeAsync_idle_start_without_end_does_not_count()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(3), UsageEventType.Enabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(0, result.IdleResetCount);
    }

    [Fact]
    public async Task ComputeAsync_counts_passive_pause()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.PassivePauseDetected, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(1), UsageEventType.PassivePauseDetected, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Success, result.Status);
        Assert.Equal(2, result.PassivePauseDetectedCount);
    }

    [Fact]
    public async Task ComputeAsync_counts_reminder_dismissal_results()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.Snoozed)));
        repo.Events.Add(new UsageEvent(2, baseTime.AddMinutes(1), UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.Ignored)));
        repo.Events.Add(new UsageEvent(3, baseTime.AddMinutes(2), UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.AutoDismissed)));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Success, result.Status);
        Assert.Equal(1, result.SnoozedCount);
        Assert.Equal(1, result.IgnoredCount);
        Assert.Equal(1, result.AutoDismissedCount);
    }

    [Fact]
    public async Task ComputeAsync_tracks_work_time()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(11), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_work_time_excludes_break()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(10), UsageEventType.BreakStarted, null));
        repo.Events.Add(new UsageEvent(3, startOfDay.AddHours(10).AddMinutes(20), UsageEventType.BreakCompleted, null));
        repo.Events.Add(new UsageEvent(4, startOfDay.AddHours(11), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(40), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_longest_continuous_work()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(10), UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(3, startOfDay.AddHours(10).AddMinutes(10), UsageEventType.IdleEnded, null));
        repo.Events.Add(new UsageEvent(4, startOfDay.AddHours(10).AddMinutes(30), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(1), result.LongestContinuousWork);
    }

    [Fact]
    public async Task ComputeAsync_average_work_cycle()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(10), UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(3, startOfDay.AddHours(10), UsageEventType.IdleEnded, null));
        repo.Events.Add(new UsageEvent(4, startOfDay.AddHours(11), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.True(result.AverageWorkCycleDuration.HasValue);
        Assert.Equal(TimeSpan.FromHours(1), result.AverageWorkCycleDuration.Value);
    }

    [Fact]
    public async Task ComputeAsync_tracks_debt_level_history()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.RestDebtLevelChanged,
            new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level1)));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(10), UsageEventType.RestDebtLevelChanged,
            new RestDebtLevelChangedPayload(RestDebtLevel.Level1, RestDebtLevel.Level2)));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(2, result.DebtLevelHistory.Count);
        Assert.Equal(RestDebtLevel.Level0, result.DebtLevelHistory[0].Previous);
        Assert.Equal(RestDebtLevel.Level1, result.DebtLevelHistory[0].Current);
        Assert.Equal(RestDebtLevel.Level1, result.DebtLevelHistory[1].Previous);
        Assert.Equal(RestDebtLevel.Level2, result.DebtLevelHistory[1].Current);
    }

    [Fact]
    public async Task ComputeAsync_pairs_reminder_with_dismissal()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderShown, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddSeconds(5), UsageEventType.ReminderDismissed,
            new ReminderDismissedPayload(ReminderResult.Snoozed)));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Single(result.ReminderOutcomes);
        Assert.Equal(ReminderOutcome.Snoozed, result.ReminderOutcomes[0].Outcome);
    }

    [Fact]
    public async Task ComputeAsync_pairs_reminder_with_break_started()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderShown, null));
        repo.Events.Add(new UsageEvent(2, baseTime.AddSeconds(3), UsageEventType.BreakStarted, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Single(result.ReminderOutcomes);
        Assert.Equal(ReminderOutcome.BreakStarted, result.ReminderOutcomes[0].Outcome);
    }

    [Fact]
    public async Task ComputeAsync_unpaired_reminder_has_null_outcome()
    {
        var repo = new FakeUsageEventRepository();
        var baseTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, baseTime, UsageEventType.ReminderShown, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Single(result.ReminderOutcomes);
        Assert.Null(result.ReminderOutcomes[0].Outcome);
    }

    [Fact]
    public async Task ComputeAsync_initial_state_from_preday_enabled()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddDays(-1).AddHours(23), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(1), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(1), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_initial_state_from_preday_disabled()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddDays(-1).AddHours(23), UsageEventType.Disabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(1), UsageEventType.Enabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(23), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_cross_midnight_session_counts_work()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddDays(-1).AddHours(22), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(2), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_repository_failure_returns_failure()
    {
        var repo = new FakeUsageEventRepository(throwOnQuery: true);
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(DailyStatisticsStatus.Failure, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ComputeAsync_longest_work_handles_single_segment()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(12), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(3), result.LongestContinuousWork);
        Assert.Equal(TimeSpan.FromHours(3), result.AverageWorkCycleDuration);
    }

    [Fact]
    public async Task ComputeAsync_work_excludes_focus_mode()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddHours(9), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(10), UsageEventType.FocusModeStarted, null));
        repo.Events.Add(new UsageEvent(3, startOfDay.AddHours(10).AddMinutes(30), UsageEventType.FocusModeEnded, null));
        repo.Events.Add(new UsageEvent(4, startOfDay.AddHours(11), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(30)), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_timezone_converts_correctly()
    {
        var repo = new FakeUsageEventRepository();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var startOfDayInTokyo = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.FromHours(9));

        var eventTime = startOfDayInTokyo.AddHours(10);
        repo.Events.Add(new UsageEvent(1, eventTime, UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, eventTime.AddHours(2), UsageEventType.Disabled, null));

        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_negative_offset_timezone()
    {
        var repo = new FakeUsageEventRepository();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("US Eastern Standard Time");

        var estMidnight = new DateTimeOffset(2026, 1, 15, 5, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, estMidnight.AddHours(5), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, estMidnight.AddHours(8), UsageEventType.Disabled, null));

        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 1, 15);

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(TimeSpan.FromHours(3), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_dst_spring_forward_boundary()
    {
        var repo = new FakeUsageEventRepository();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("US Eastern Standard Time");

        var utcMidnight = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Unspecified), tz);

        repo.Events.Add(new UsageEvent(1, utcMidnight.AddHours(5), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, utcMidnight.AddHours(6), UsageEventType.Disabled, null));

        var service = new DailyStatisticsService(repo);
        var result = await service.ComputeAsync(new DateOnly(2026, 3, 8), tz);

        Assert.Equal(TimeSpan.FromHours(1), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_dst_fall_back_boundary()
    {
        var repo = new FakeUsageEventRepository();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("US Eastern Standard Time");

        var utcMidnight = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified), tz);

        repo.Events.Add(new UsageEvent(1, utcMidnight.AddHours(4), UsageEventType.Enabled, null));
        repo.Events.Add(new UsageEvent(2, utcMidnight.AddHours(6), UsageEventType.Disabled, null));

        var service = new DailyStatisticsService(repo);
        var result = await service.ComputeAsync(new DateOnly(2026, 11, 1), tz);

        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWorkTime);
    }

    [Fact]
    public async Task ComputeAsync_preday_idle_completes_in_day()
    {
        var repo = new FakeUsageEventRepository();
        var startOfDay = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        repo.Events.Add(new UsageEvent(1, startOfDay.AddDays(-1).AddHours(23), UsageEventType.IdleStarted, null));
        repo.Events.Add(new UsageEvent(2, startOfDay.AddHours(2), UsageEventType.IdleEnded, null));
        repo.Events.Add(new UsageEvent(3, startOfDay.AddHours(3), UsageEventType.Paused, null));
        repo.Events.Add(new UsageEvent(4, startOfDay.AddHours(4), UsageEventType.Resumed, null));
        repo.Events.Add(new UsageEvent(5, startOfDay.AddHours(5), UsageEventType.Disabled, null));
        var service = new DailyStatisticsService(repo);
        var date = new DateOnly(2026, 7, 15);
        var tz = TimeZoneInfo.Utc;

        var result = await service.ComputeAsync(date, tz);

        Assert.Equal(1, result.IdleResetCount);
        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWorkTime);
    }

    private sealed class FakeUsageEventRepository : IUsageEventRepository
    {
        private readonly bool throwOnQuery;

        public List<UsageEvent> Events { get; } = [];

        public FakeUsageEventRepository(bool throwOnQuery = false)
        {
            this.throwOnQuery = throwOnQuery;
        }

        public Task WriteAsync(UsageEventType eventType, DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            if (throwOnQuery)
                throw new InvalidOperationException("Simulated query failure.");

            var result = Events
                .Where(e => e.OccurredUtc >= from && e.OccurredUtc < to)
                .OrderBy(e => e.OccurredUtc)
                .ThenBy(e => e.Id)
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<UsageEvent>>(result);
        }
    }
}
