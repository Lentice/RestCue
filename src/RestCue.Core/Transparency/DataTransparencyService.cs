namespace RestCue.Core.Transparency;

using Settings;
using UsageEvents;

public sealed class DataTransparencyService : IDataTransparencyService
{
    private readonly ISettingsRepository settingsRepository;
    private readonly IUsageEventMetadataReader metadataReader;
    private readonly string databasePath;

    private static readonly string[] NeverCollectedLabels =
    [
        "window title",
        "鍵盤輸入內容",
        "剪貼簿",
        "畫面",
        "網址",
        "文件名稱",
        "camera",
        "microphone",
        "mouse trajectory",
        "input / mouse pattern analysis"
    ];

    public DataTransparencyService(
        ISettingsRepository settingsRepository,
        IUsageEventMetadataReader metadataReader,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.settingsRepository = settingsRepository;
        this.metadataReader = metadataReader;
        this.databasePath = databasePath;
    }

    public async Task<DataTransparencySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        UsageEventMetadata metadata;
        try
        {
            metadata = await metadataReader.ReadMetadataAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return CreateUnavailableSnapshot($"Failed to read usage event metadata: {ex.Message}");
        }

        SettingsLoadResult settingsResult;
        try
        {
            settingsResult = await settingsRepository.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return CreateUnavailableSnapshot($"Failed to load settings: {ex.Message}");
        }

        if (settingsResult.RecoveredFromCorruption)
        {
            return CreateUnavailableSnapshot("Settings were recovered from corruption; data state cannot be confirmed.");
        }

        var settings = settingsResult.Settings;

        long? databaseSizeBytes = null;
        try
        {
            var fileInfo = new System.IO.FileInfo(databasePath);
            if (fileInfo.Exists)
                databaseSizeBytes = fileInfo.Length;
        }
        catch
        {
        }

        var categories = new List<DataCategoryStatus>
        {
            new(
                "Last input elapsed time for activity detection",
                CollectionState.EnabledWithData,
                "Used to determine activity state; no content is recorded"),
            new(
                "Foreground process name collection",
                settings.CollectForegroundProcessNames
                    ? CollectionState.EnabledEmpty
                    : CollectionState.DisabledByUser,
                settings.CollectForegroundProcessNames
                    ? "Opt-in enabled but data is only in memory, never persisted"
                    : "Opt-in is disabled by default"),
        };

        foreach (var label in NeverCollectedLabels)
        {
            categories.Add(new DataCategoryStatus(
                label,
                CollectionState.NeverCollected,
                null));
        }

        var typeCounts = Enum.GetValues<UsageEventType>()
            .Select(t => new UsageEventTypeCount(
                t,
                metadata.PerTypeCounts.TryGetValue(t, out var count) ? count : 0))
            .ToList();

        return new DataTransparencySnapshot(
            categories.AsReadOnly(),
            typeCounts.AsReadOnly(),
            metadata.TotalCount,
            metadata.EarliestUtc,
            metadata.LatestUtc,
            databaseSizeBytes,
            metadata.LastExportUtc,
            metadata.LastClearUtc,
            null);
    }

    private static DataTransparencySnapshot CreateUnavailableSnapshot(string message)
    {
        var unavailable = new DataCategoryStatus(
            "All data categories",
            CollectionState.Unavailable,
            message);

        return new DataTransparencySnapshot(
            [unavailable],
            [],
            0,
            null,
            null,
            null,
            null,
            null,
            message);
    }
}
