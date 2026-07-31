using RestCue.App.Lifecycle;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// Stored settings must never be able to deny the user the product. If the reminder
/// engine cannot be built from them, startup degrades to defaults and records a
/// diagnostic instead of exiting.
/// </summary>
public sealed class ApplicationStartupEngineFallbackTests
{
    [Fact]
    public async Task Engine_construction_failure_falls_back_to_defaults_with_a_diagnostic()
    {
        AppSettings persisted = AppSettings.Default with { WorkInterval = TimeSpan.FromMinutes(25) };
        var startup = await CreateStartupAsync(persisted);
        var diagnostics = new List<string>();

        AppSettings resolved = startup.ResolveEngineSettings(
            _ => throw new ArgumentOutOfRangeException("workInterval", "refused by a construction guard"),
            diagnostics.Add);

        Assert.Equal(AppSettings.Default, resolved);
        Assert.Equal(AppSettings.Default, startup.CurrentSettings);
        Assert.True(startup.FellBackToDefaultSettings);
        string diagnostic = Assert.Single(diagnostics);
        Assert.Contains(ApplicationStartup.EngineFallbackDiagnostic, diagnostic);
        Assert.Contains("refused by a construction guard", diagnostic);
    }

    [Fact]
    public async Task Valid_settings_do_not_trigger_the_fallback()
    {
        AppSettings persisted = AppSettings.Default with { WorkInterval = TimeSpan.FromMinutes(25) };
        var startup = await CreateStartupAsync(persisted);
        var diagnostics = new List<string>();

        AppSettings resolved = startup.ResolveEngineSettings(
            settings => WorkCycleTrackerFactory.Create(settings, new FakeClock()),
            diagnostics.Add);

        Assert.Equal(persisted, resolved);
        Assert.Equal(persisted, startup.CurrentSettings);
        Assert.False(startup.FellBackToDefaultSettings);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Default_settings_build_a_working_engine()
    {
        var startup = await CreateStartupAsync(AppSettings.Default);
        var diagnostics = new List<string>();

        AppSettings resolved = startup.ResolveEngineSettings(
            settings => WorkCycleTrackerFactory.Create(settings, new FakeClock()),
            diagnostics.Add);

        Assert.Equal(AppSettings.Default, resolved);
        Assert.False(startup.FellBackToDefaultSettings);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Fallback_settings_still_build_a_working_engine()
    {
        var startup = await CreateStartupAsync(
            AppSettings.Default with { WorkInterval = TimeSpan.FromMinutes(25) });

        startup.ResolveEngineSettings(_ => throw new InvalidOperationException("boom"), _ => { });

        // The degraded settings must be runnable, or the fallback has bought nothing.
        var tracker = WorkCycleTrackerFactory.Create(startup.CurrentSettings, new FakeClock());
        Assert.Equal(WorkCyclePhase.Working, tracker.CurrentPhase);
    }

    private static async Task<ApplicationStartup> CreateStartupAsync(AppSettings persisted)
    {
        var startup = new ApplicationStartup(new FakeSettingsRepository(persisted), new NoOpLifecycle());
        await startup.InitializeAsync();
        return startup;
    }

    private sealed class FakeSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(settings));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpLifecycle : IApplicationLifecycle
    {
        public void Start()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed => TimeSpan.Zero;
    }
}
