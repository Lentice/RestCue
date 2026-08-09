using System.Windows;
using RestCue.Core.Transparency;

namespace RestCue.App;

public sealed partial class TransparencyWindow : Window
{
    private readonly IDataTransparencyService transparencyService;

    public TransparencyWindow(IDataTransparencyService transparencyService)
    {
        ArgumentNullException.ThrowIfNull(transparencyService);
        this.transparencyService = transparencyService;
        InitializeComponent();
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        DataTransparencySnapshot snapshot;
        try
        {
            snapshot = await transparencyService.GetSnapshotAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"無法載入資料透明檢視: {ex.Message}";
            StatusBanner.Visibility = Visibility.Visible;
            return;
        }

        if (snapshot.UnavailableMessage != null)
        {
            StatusText.Text = snapshot.UnavailableMessage;
            StatusBanner.Visibility = Visibility.Visible;
            CategoryCard.Visibility = Visibility.Collapsed;
            return;
        }

        CategoryList.ItemsSource = snapshot.Categories
            .Select(c => new
            {
                Label = c.Label,
                State = FormatCollectionState(c.State),
                Detail = string.IsNullOrWhiteSpace(c.Detail) ? null : c.Detail
            })
            .ToList();

        TotalCountText.Text = $"總筆數: {snapshot.TotalEventCount}";
        TotalCountText.Visibility = Visibility.Visible;

        EventTypeCountList.ItemsSource = snapshot.EventTypeCounts
            .Select(e => new
            {
                EventType = e.EventType.ToString(),
                e.Count
            })
            .ToList();

        EarliestText.Text = snapshot.EarliestUtc.HasValue
            ? $"最早事件: {snapshot.EarliestUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "最早事件: (無資料)";
        LatestText.Text = snapshot.LatestUtc.HasValue
            ? $"最新事件: {snapshot.LatestUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "最新事件: (無資料)";

        DatabaseSizeText.Text = snapshot.DatabaseSizeBytes.HasValue
            ? $"資料庫大小: {FormatFileSize(snapshot.DatabaseSizeBytes.Value)}"
            : "資料庫大小: (無法讀取)";
        LastExportText.Text = snapshot.LastExportUtc.HasValue
            ? $"最後匯出: {snapshot.LastExportUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "最後匯出: (從未匯出)";
        LastClearText.Text = snapshot.LastClearUtc.HasValue
            ? "最後清除: " + $"{snapshot.LastClearUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "最後清除: (從未清除)";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatCollectionState(CollectionState state)
    {
        return state switch
        {
            CollectionState.NeverCollected => "永不收集",
            CollectionState.DisabledByUser => "未啟用",
            CollectionState.EnabledEmpty => "已啟用，目前 0 筆",
            CollectionState.EnabledWithData => "已啟用",
            CollectionState.Unavailable => "無法確認",
            _ => "未知"
        };
    }
}
