using RestCue.App.Lifecycle;
using RestCue.Core.Settings;
using Xunit;

namespace RestCue.App.Tests;

public sealed class ApplicationStartupTests
{
    [Fact]
    public async Task Initialize_loads_persisted_settings_before_starting_lifecycle()
    {
        AppSettings persisted = AppSettings.Default with { WorkInterval = TimeSpan.FromMinutes(42) };
        var repository = new FakeSettingsRepository(persisted);
        var lifecycle = new RecordingLifecycle(() => Assert.True(repository.Loaded));
        var startup = new ApplicationStartup(repository, lifecycle);

        SettingsLoadResult result = await startup.InitializeAsync();

        Assert.Equal(persisted, result.Settings);
        Assert.Equal(persisted, startup.CurrentSettings);
        Assert.True(lifecycle.Started);
    }

    private sealed class FakeSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        public bool Loaded { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loaded = true;
            return Task.FromResult(new SettingsLoadResult(settings));
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLifecycle(Action onStart) : IApplicationLifecycle
    {
        public bool Started { get; private set; }

        public void Start()
        {
            onStart();
            Started = true;
        }

        public void Dispose()
        {
        }
    }
}
