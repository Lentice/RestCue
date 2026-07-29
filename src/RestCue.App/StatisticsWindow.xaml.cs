using System.Windows;
using RestCue.Core.UsageEvents;

namespace RestCue.App;

public sealed partial class StatisticsWindow : Window
{
    private readonly IDailyStatisticsService statisticsService;

    public StatisticsWindow(IDailyStatisticsService statisticsService)
    {
        ArgumentNullException.ThrowIfNull(statisticsService);
        this.statisticsService = statisticsService;
        InitializeComponent();
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var today = DateOnly.FromDateTime(DateTime.Now);
        DateText.Text = $"日期: {today:yyyy-MM-dd}";

        var timezone = TimeZoneInfo.Local;

        DailyStatistics stats;
        try
        {
            stats = await statisticsService.ComputeAsync(today, timezone);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"無法載入統計: {ex.Message}";
            return;
        }

        switch (stats.Status)
        {
            case DailyStatisticsStatus.Failure:
                StatusText.Text = $"查詢失敗: {stats.ErrorMessage}";
                return;

            case DailyStatisticsStatus.PartialData:
                StatusText.Text = "部分資料可能不完整（部分事件無法解析）。";
                break;

            case DailyStatisticsStatus.Success:
                StatusText.Visibility = Visibility.Collapsed;
                break;
        }

        WorkTimeText.Text = FormatTimeSpan(stats.EffectiveWorkTime);
        LongestWorkText.Text = FormatTimeSpan(stats.LongestContinuousWork);
        AvgCycleText.Text = stats.AverageWorkCycleDuration.HasValue
            ? FormatTimeSpan(stats.AverageWorkCycleDuration.Value)
            : "沒有已結束的工作區段";

        BreakCompletedText.Text = stats.BreakCompletedCount.ToString();
        IdleResetText.Text = stats.IdleResetCount.ToString();
        PassivePauseText.Text = stats.PassivePauseDetectedCount.ToString();
        SnoozedText.Text = stats.SnoozedCount.ToString();
        IgnoredText.Text = stats.IgnoredCount.ToString();
        AutoDismissedText.Text = stats.AutoDismissedCount.ToString();

        if (stats.DebtLevelHistory.Count > 0)
        {
            DebtHistoryText.Text = string.Join("\n",
                stats.DebtLevelHistory.Select(d =>
                    $"{d.OccurredUtc.LocalDateTime:HH:mm:ss} - Level {(int)d.Previous} → Level {(int)d.Current}"));
        }
        else
        {
            DebtHistoryText.Text = "今日無債務等級變化。";
        }

        if (stats.ReminderOutcomes.Count > 0)
        {
            ReminderOutcomesText.Text = string.Join("\n",
                stats.ReminderOutcomes.Select(r =>
                {
                    var outcome = r.Outcome.HasValue
                        ? r.Outcome.Value switch
                        {
                            ReminderOutcome.Snoozed => "延後",
                            ReminderOutcome.Ignored => "忽略",
                            ReminderOutcome.AutoDismissed => "逾時未回應",
                            ReminderOutcome.BreakStarted => "開始休息",
                            _ => "未知"
                        }
                        : "未完成";
                    return $"{r.ReminderShownAt.LocalDateTime:HH:mm:ss} → {outcome}";
                }));
        }
        else
        {
            ReminderOutcomesText.Text = "今日無提醒事件。";
        }

        if (stats.PerAppWorkTime.Count > 0)
        {
            PerAppText.Text = string.Join("\n",
                stats.PerAppWorkTime
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}: {FormatTimeSpan(kv.Value)}"));
        }
        else
        {
            PerAppHeader.Visibility = Visibility.Collapsed;
            PerAppText.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} 小時 {ts.Minutes} 分鐘";
        return $"{ts.Minutes} 分鐘 {ts.Seconds} 秒";
    }
}
