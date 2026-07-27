using Microsoft.Data.Sqlite;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Settings;
using Xunit;

namespace RestCue.Infrastructure.Tests.Settings;

public sealed class SqliteSettingsRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Valid_settings_survive_a_new_repository_instance()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var saved = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(35),
            CollectForegroundProcessNames = true,
        };

        var firstRun = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await firstRun.SaveAsync(saved);

        var restartedApp = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        SettingsLoadResult result = await restartedApp.LoadAsync();

        Assert.Equal(saved, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task Creates_the_product_contract_settings_schema()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());

        await repository.LoadAsync();

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'settings';";
        string schema = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("key TEXT PRIMARY KEY", schema);
        Assert.Contains("value TEXT NOT NULL", schema);
        Assert.Contains("updated_at_utc TEXT NOT NULL", schema);
    }

    [Fact]
    public async Task Corrupted_database_is_backed_up_and_replaced_with_safe_defaults()
    {
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "restcue.db");
        await File.WriteAllTextAsync(databasePath, "definitely not SQLite");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.CorruptBackupPath);
        Assert.True(File.Exists(result.CorruptBackupPath));
        Assert.Equal("definitely not SQLite", await File.ReadAllTextAsync(result.CorruptBackupPath));

        SettingsLoadResult nextLoad = await repository.LoadAsync();
        Assert.Equal(AppSettings.Default, nextLoad.Settings);
        Assert.False(nextLoad.RecoveredFromCorruption);
    }

    [Fact]
    public async Task Invalid_cross_field_settings_are_rejected_without_replacing_saved_settings()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
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
