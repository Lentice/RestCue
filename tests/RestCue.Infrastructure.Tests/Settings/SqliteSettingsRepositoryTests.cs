using System.Text.Json;
using Microsoft.Data.Sqlite;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
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
            DebtLevel2Threshold = TimeSpan.FromMinutes(40),
            DebtLevel3Threshold = TimeSpan.FromMinutes(50),
            DebtLevel4Threshold = TimeSpan.FromMinutes(60),
            CollectForegroundProcessNames = true,
            DebtLevelTrayNotificationEnabled = false,
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

    [Fact]
    public async Task Locked_database_propagates_operational_error_without_deleting_valid_settings()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        var saved = AppSettings.Default with
        {
            WorkInterval = TimeSpan.FromMinutes(37),
            DebtLevel2Threshold = TimeSpan.FromMinutes(40),
        };
        await repository.SaveAsync(saved);

        await using (var lockConnection = new SqliteConnection(
                         $"Data Source={databasePath};Pooling=False;Default Timeout=1"))
        {
            await lockConnection.OpenAsync();
            await using SqliteCommand lockCommand = lockConnection.CreateCommand();
            lockCommand.CommandText = "BEGIN EXCLUSIVE;";
            await lockCommand.ExecuteNonQueryAsync();

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => repository.LoadAsync());

            Assert.Contains(exception.SqliteErrorCode, new[] { 5, 6 });
            Assert.True(File.Exists(databasePath));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }

        Assert.Equal(saved, (await repository.LoadAsync()).Settings);
    }

    [Fact]
    public async Task RetryCooldown_round_trips_through_save_and_load()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var saved = AppSettings.Default with
        {
            RetryCooldown = TimeSpan.FromMinutes(42),
        };

        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(saved);

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(42), result.Settings.RetryCooldown);
    }

    [Fact]
    public async Task FocusModeDuration_round_trips_through_save_and_load()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var saved = AppSettings.Default with
        {
            FocusModeDuration = TimeSpan.FromMinutes(90),
        };

        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(saved);

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(90), result.Settings.FocusModeDuration);
    }

    [Fact]
    public async Task Stored_out_of_range_FocusModeDuration_degrades_to_defaults()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        string jsonWithBadFocusModeDuration =
            /* language=json */ """{"schemaVersion":2,"focusModeDuration":"-00:10:00"}""";
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                "UPDATE settings SET value = @value WHERE key = 'app_settings';";
            updateCommand.Parameters.AddWithValue("@value", jsonWithBadFocusModeDuration);
            await updateCommand.ExecuteNonQueryAsync();
        }

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.Null(result.CorruptBackupPath);
    }

    [Fact]
    public async Task Older_settings_without_RetryCooldown_loads_default_20_minutes()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        string jsonWithoutRetryCooldown = /* language=json */ """{"workInterval":"00:15:00"}""";
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE settings SET value = @value WHERE key = 'app_settings';";
        updateCommand.Parameters.AddWithValue("@value", jsonWithoutRetryCooldown);
        await updateCommand.ExecuteNonQueryAsync();

        var reloaded = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        SettingsLoadResult result = await reloaded.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(20), result.Settings.RetryCooldown);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Settings.WorkInterval);
    }

    [Fact]
    public async Task Debt_thresholds_and_break_guide_mode_round_trip()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var saved = AppSettings.Default with
        {
            DebtLevel2Threshold = TimeSpan.FromMinutes(40),
            DebtLevel3Threshold = TimeSpan.FromMinutes(55),
            DebtLevel4Threshold = TimeSpan.FromMinutes(70),
            BreakGuideMode = BreakGuideMode.Voice,
        };

        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(saved);

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(40), result.Settings.DebtLevel2Threshold);
        Assert.Equal(TimeSpan.FromMinutes(55), result.Settings.DebtLevel3Threshold);
        Assert.Equal(TimeSpan.FromMinutes(70), result.Settings.DebtLevel4Threshold);
        Assert.Equal(BreakGuideMode.Voice, result.Settings.BreakGuideMode);
    }

    [Fact]
    public async Task Older_document_without_debt_thresholds_loads_v13_defaults()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        string minimalJson = /* language=json */ """{"workInterval":"00:15:00"}""";
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE settings SET value = @value WHERE key = 'app_settings';";
        updateCommand.Parameters.AddWithValue("@value", minimalJson);
        await updateCommand.ExecuteNonQueryAsync();

        var reloaded = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        SettingsLoadResult result = await reloaded.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(35), result.Settings.DebtLevel2Threshold);
        Assert.Equal(TimeSpan.FromMinutes(45), result.Settings.DebtLevel3Threshold);
        Assert.Equal(TimeSpan.FromMinutes(60), result.Settings.DebtLevel4Threshold);
        Assert.Equal(BreakGuideMode.Cue, result.Settings.BreakGuideMode);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Settings.WorkInterval);
        Assert.False(result.Settings.CollectForegroundProcessNames);
    }

    [Fact]
    public async Task Unknown_extra_json_field_is_ignored()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        string jsonWithExtra = /* language=json */ """{"workInterval":"00:15:00","futureField":1}""";
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE settings SET value = @value WHERE key = 'app_settings';";
        updateCommand.Parameters.AddWithValue("@value", jsonWithExtra);
        await updateCommand.ExecuteNonQueryAsync();

        var reloaded = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        SettingsLoadResult result = await reloaded.LoadAsync();

        Assert.False(result.RecoveredFromCorruption);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Settings.WorkInterval);
    }

    [Fact]
    public async Task Invalid_debt_combination_is_rejected_without_replacing_saved_settings()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);
        var invalid = AppSettings.Default with
        {
            DebtLevel2Threshold = TimeSpan.FromMinutes(20),
        };

        SettingsValidationException exception = await Assert.ThrowsAsync<SettingsValidationException>(
            () => repository.SaveAsync(invalid));

        Assert.Contains(exception.Errors, error => error.Field == "DebtLevel2Threshold");
        Assert.Equal(AppSettings.Default, (await repository.LoadAsync()).Settings);
    }

    [Fact]
    public async Task Document_version_upgrade_does_not_change_sqlite_user_version()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        string minimalJson = /* language=json */ """{"workInterval":"00:15:00"}""";
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE settings SET value = @value WHERE key = 'app_settings';";
        updateCommand.Parameters.AddWithValue("@value", minimalJson);
        await updateCommand.ExecuteNonQueryAsync();

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        long versionBefore = (long)(await versionCommand.ExecuteScalarAsync() ?? 0L);

        var reloaded = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await reloaded.LoadAsync();

        await using var versionAfterCommand = connection.CreateCommand();
        versionAfterCommand.CommandText = "PRAGMA user_version;";
        long versionAfter = (long)(await versionAfterCommand.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(versionBefore, versionAfter);
    }

    [Fact]
    public async Task Invalid_settings_json_recovers_settings_only_preserving_usage_events()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());
        await repository.SaveAsync(AppSettings.Default);

        var eventRepo = new SqliteUsageEventRepository(databasePath);
        await eventRepo.WriteAsync(Core.UsageEvents.UsageEventType.BreakCompleted,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE settings SET value = 'not valid json' WHERE key = 'app_settings';";
            await cmd.ExecuteNonQueryAsync();
        }

        SettingsLoadResult result = await repository.LoadAsync();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);

        var events = await eventRepo.QueryAsync(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Single(events);
        Assert.Equal(Core.UsageEvents.UsageEventType.BreakCompleted, events[0].EventType);

        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_without_downgrade_or_deletion()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);
        await using (var connection = new SqliteConnection(
                         $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            await command.ExecuteNonQueryAsync();
        }

        var repository = new SqliteSettingsRepository(databasePath, new AppSettingsValidator());

        await Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(
            () => repository.LoadAsync());

        await using var verifyConnection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await verifyConnection.OpenAsync();
        await using SqliteCommand verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(99L, await verifyCommand.ExecuteScalarAsync());
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
