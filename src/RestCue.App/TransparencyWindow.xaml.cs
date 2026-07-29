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
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        if (snapshot.UnavailableMessage != null)
        {
            StatusText.Text = snapshot.UnavailableMessage;
            StatusText.Visibility = Visibility.Visible;
            CategoryList.Visibility = Visibility.Collapsed;
            return;
        }

        CategoryList.ItemsSource = snapshot.Categories
            .Select(c => new
            {
                Label = c.Label,
                State = FormatCollectionState(c.State)
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
