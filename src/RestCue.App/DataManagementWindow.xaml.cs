using System.Windows;
using RestCue.Core.DataManagement;
using RestCue.Core.Settings;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.DataManagement;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;

namespace RestCue.App;

public sealed partial class DataManagementWindow : Window
{
    private readonly IUsageEventRepository usageEventRepository;
    private readonly ISettingsRepository settingsRepository;

    public DataManagementWindow(
        IUsageEventRepository usageEventRepository,
        ISettingsRepository settingsRepository)
    {
        ArgumentNullException.ThrowIfNull(usageEventRepository);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        this.usageEventRepository = usageEventRepository;
        this.settingsRepository = settingsRepository;
        InitializeComponent();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "匯出使用記錄",
            FileName = $"restcue-usage-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json",
            Filter = "JSON 檔 (*.json)|*.json|所有檔案 (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        ExportButton.IsEnabled = false;
        ExportStatusText.Text = "匯出中…";

        try
        {
            using var writer = new AtomicJsonExportWriter(dialog.FileName);
            var exporter = new UsageDataExporter(usageEventRepository, writer, SchemaMigrator.LatestSchemaVersion);
            var result = await exporter.ExportAsync(
                dialog.FileName,
                DateTimeOffset.MinValue,
                DateTimeOffset.MaxValue);

            if (result.Succeeded)
            {
                ExportStatusText.Text = $"已匯出至: {result.WrittenPath}";
            }
            else
            {
                ExportStatusText.Text = $"匯出失敗: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = $"匯出失敗: {ex.Message}";
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "確定要清除所有使用記錄？\n\n" +
            "已儲存的休息記錄、工作時間統計、提醒結果等資料將全部刪除。\n" +
            "設定不受影響。此操作不可復原。",
            "清除使用記錄",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        ClearHistoryButton.IsEnabled = false;
        ClearHistoryStatusText.Text = "清除中…";

        try
        {
            var maintenance = new SqliteUsageDataMaintenance(
                Infrastructure.Settings.LocalSettingsPaths.DatabaseFile);
            var result = await maintenance.ClearUsageHistoryAsync();

            if (result.Succeeded)
            {
                ClearHistoryStatusText.Text = $"已清除 {result.AffectedRowCount} 筆記錄。";
                OnDataCleared();
            }
            else
            {
                ClearHistoryStatusText.Text = $"清除失敗: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            ClearHistoryStatusText.Text = $"清除失敗: {ex.Message}";
        }
        finally
        {
            ClearHistoryButton.IsEnabled = true;
        }
    }

    private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "確定要重設所有設定？\n\n" +
            "所有設定將恢復為預設值，包含工作間隔、休息時間、通知行為等。\n" +
            "前景程式名稱蒐集將關閉。\n" +
            "使用記錄不受影響。此操作不可復原。",
            "重設設定",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        ResetSettingsButton.IsEnabled = false;
        ResetSettingsStatusText.Text = "重設中…";

        try
        {
            await settingsRepository.SaveAsync(AppSettings.Default);
            ResetSettingsStatusText.Text = "設定已重設。部分設定將於下次啟動時生效。";
            OnSettingsReset();
        }
        catch (Exception ex)
        {
            ResetSettingsStatusText.Text = $"重設失敗: {ex.Message}";
        }
        finally
        {
            ResetSettingsButton.IsEnabled = true;
        }
    }

    public event EventHandler? DataCleared;

    public event EventHandler? SettingsReset;

    private void OnDataCleared()
    {
        DataCleared?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsReset()
    {
        SettingsReset?.Invoke(this, EventArgs.Empty);
    }
}
