namespace RestCue.Core.UsageEvents;

public interface IDailyStatisticsService
{
    Task<DailyStatistics> ComputeAsync(
        DateOnly date,
        TimeZoneInfo timezone,
        CancellationToken cancellationToken = default);
}
