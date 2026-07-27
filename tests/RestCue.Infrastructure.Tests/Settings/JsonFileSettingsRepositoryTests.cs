using RestCue.Core.Settings;
using RestCue.Infrastructure.Settings;
using Xunit;

namespace RestCue.Infrastructure.Tests.Settings;

public sealed class JsonFileSettingsRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Valid_settings_survive_a_new_repository_instance()
    {
        string settingsPath = Path.Combine(directory, "settings.json");
        var saved = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(35),
            CollectForegroundProcessNames = true,
        };

        var firstRun = new JsonFileSettingsRepository(settingsPath, new AppSettingsValidator());
        await firstRun.SaveAsync(saved);

        var restartedApp = new JsonFileSettingsRepository(settingsPath, new AppSettingsValidator());
        SettingsLoadResult result = await restartedApp.LoadAsync();

        Assert.Equal(saved, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.CorruptBackupPath);
    }

    [Fact]
    public async Task Corrupted_settings_are_backed_up_and_replaced_with_safe_defaults()
    {
        Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ definitely not JSON");
        var repository = new JsonFileSettingsRepository(settingsPath, new AppSettingsValidator());

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.CorruptBackupPath);
        Assert.True(File.Exists(result.CorruptBackupPath));
        Assert.Equal("{ definitely not JSON", await File.ReadAllTextAsync(result.CorruptBackupPath));

        SettingsLoadResult nextLoad = await repository.LoadAsync();
        Assert.Equal(AppSettings.Default, nextLoad.Settings);
        Assert.False(nextLoad.RecoveredFromCorruption);
    }

    [Fact]
    public async Task Invalid_cross_field_settings_are_rejected_without_replacing_saved_settings()
    {
        string settingsPath = Path.Combine(directory, "settings.json");
        var repository = new JsonFileSettingsRepository(settingsPath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);
        var invalid = AppSettings.Default with
        {
            IdleThreshold = TimeSpan.FromMinutes(1),
            PassiveBreakThreshold = TimeSpan.FromSeconds(61),
        };

        SettingsValidationException exception = await Assert.ThrowsAsync<SettingsValidationException>(
            () => repository.SaveAsync(invalid));

        Assert.Contains(exception.Errors, error => error.Field == "PassiveBreakThreshold");
        Assert.Equal(AppSettings.Default, (await repository.LoadAsync()).Settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
