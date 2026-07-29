using RestCue.Core.Settings;
using RestCue.Core.Transparency;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.Core.Tests.Transparency;

public sealed class DataTransparencyServiceTests
{
    [Fact]
    public async Task Empty_database_reports_zero_counts_and_no_range()
    {
        var (service, _) = CreateService();
        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(0, snapshot.TotalEventCount);
        Assert.Null(snapshot.EarliestUtc);
        Assert.Null(snapshot.LatestUtc);
        Assert.Null(snapshot.UnavailableMessage);
        Assert.All(snapshot.EventTypeCounts, e => Assert.Equal(0, e.Count));
    }

    [Fact]
    public async Task All_event_types_are_listed_from_enum()
    {
        var (service, _) = CreateService();
        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(Enum.GetValues<UsageEventType>().Length, snapshot.EventTypeCounts.Count);
    }

    [Fact]
    public async Task Counts_and_range_match_written_events()
    {
        var (service, reader) = CreateService();

        var t1 = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 6, 15, 11, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        reader.AddEvent(UsageEventType.ReminderShown, t1);
        reader.AddEvent(UsageEventType.BreakCompleted, t2);
        reader.AddEvent(UsageEventType.BreakStarted, t3);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(3, snapshot.TotalEventCount);
        Assert.Equal(t1, snapshot.EarliestUtc);
        Assert.Equal(t3, snapshot.LatestUtc);
    }

    [Fact]
    public async Task Opt_in_off_reports_disabled_not_zero()
    {
        var (service, _) = CreateService(collectForegroundProcessNames: false);
        var snapshot = await service.GetSnapshotAsync();

        var fgCategory = snapshot.Categories
            .First(c => c.Label == "Foreground process name collection");
        Assert.Equal(CollectionState.DisabledByUser, fgCategory.State);
    }

    [Fact]
    public async Task Opt_in_on_with_empty_data_is_distinguishable()
    {
        var (service, _) = CreateService(collectForegroundProcessNames: true);
        var snapshot = await service.GetSnapshotAsync();

        var fgCategory = snapshot.Categories
            .First(c => c.Label == "Foreground process name collection");
        Assert.NotEqual(CollectionState.DisabledByUser, fgCategory.State);
        Assert.Equal(CollectionState.NeverCollected, fgCategory.State);
    }

    [Fact]
    public async Task Repository_unavailable_yields_Unavailable_state()
    {
        var (service, _) = CreateService(throwOnRead: true);
        var snapshot = await service.GetSnapshotAsync();

        Assert.NotNull(snapshot.UnavailableMessage);
        Assert.All(snapshot.Categories, c => Assert.Equal(CollectionState.Unavailable, c.State));
    }

    [Fact]
    public async Task Last_input_elapsed_time_always_shows_EnabledWithData()
    {
        var (service, _) = CreateService();
        var snapshot = await service.GetSnapshotAsync();

        var lastInput = snapshot.Categories
            .First(c => c.Label == "Last input elapsed time for activity detection");
        Assert.Equal(CollectionState.EnabledWithData, lastInput.State);
    }

    [Fact]
    public async Task NeverCollected_list_matches_privacy_doc()
    {
        var (service, _) = CreateService();
        var snapshot = await service.GetSnapshotAsync();

        var neverCollected = snapshot.Categories
            .Where(c => c.State == CollectionState.NeverCollected)
            .Select(c => c.Label)
            .ToList();

        Assert.Contains("window title", neverCollected);
        Assert.Contains("鍵盤輸入內容", neverCollected);
        Assert.Contains("剪貼簿", neverCollected);
        Assert.Contains("畫面", neverCollected);
        Assert.Contains("網址", neverCollected);
        Assert.Contains("文件名稱", neverCollected);
    }

    [Fact]
    public async Task Recovered_from_corruption_yields_Unavailable()
    {
        var settingsRepo = new FakeSettingsRepository(
            new SettingsLoadResult(AppSettings.Default, RecoveredFromCorruption: true));
        var reader = new FakeUsageEventMetadataReader();
        var service = new DataTransparencyService(settingsRepo, reader);
        var snapshot = await service.GetSnapshotAsync();

        Assert.NotNull(snapshot.UnavailableMessage);
    }

    private static (DataTransparencyService service, FakeUsageEventMetadataReader reader) CreateService(
        bool collectForegroundProcessNames = false,
        bool throwOnRead = false)
    {
        var settings = AppSettings.Default with
        {
            CollectForegroundProcessNames = collectForegroundProcessNames
        };
        var settingsRepo = new FakeSettingsRepository(
            new SettingsLoadResult(settings));
        var reader = new FakeUsageEventMetadataReader(throwOnRead);
        var service = new DataTransparencyService(settingsRepo, reader);
        return (service, reader);
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        private readonly SettingsLoadResult result;

        public FakeSettingsRepository(SettingsLoadResult result)
        {
            this.result = result;
        }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUsageEventMetadataReader : IUsageEventMetadataReader
    {
        private readonly bool throwOnRead;
        private readonly List<UsageEvent> events = [];

        public FakeUsageEventMetadataReader(bool throwOnRead = false)
        {
            this.throwOnRead = throwOnRead;
        }

        public void AddEvent(UsageEventType type, DateTimeOffset occurredUtc)
        {
            events.Add(new UsageEvent(events.Count + 1, occurredUtc, type, null));
        }

        public Task<UsageEventMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default)
        {
            if (throwOnRead)
                throw new InvalidOperationException("Simulated metadata read failure");

            var totalCount = events.LongCount();
            var earliest = events.Count > 0 ? events.Min(e => e.OccurredUtc) : (DateTimeOffset?)null;
            var latest = events.Count > 0 ? events.Max(e => e.OccurredUtc) : (DateTimeOffset?)null;

            var perType = events
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.LongCount());

            return Task.FromResult(new UsageEventMetadata(
                totalCount, earliest, latest, perType, 0, 2));
        }
    }
}
