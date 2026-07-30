using RestCue.Core.Reminders;
using RestCue.Infrastructure.Settings;
using Xunit;

namespace RestCue.Infrastructure.Tests.Settings;

public sealed class SqliteApplicationRuleRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saved_rule_survives_new_repository_instance()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repo = new SqliteApplicationRuleRepository(databasePath);

        var rule = new ApplicationRule
        {
            ProcessName = "test-app",
            RuleType = ApplicationRuleType.TrayOnly,
            CustomInterval = null,
        };
        await repo.SaveAsync(rule);

        var freshRepo = new SqliteApplicationRuleRepository(databasePath);
        IReadOnlyList<ApplicationRule> loaded = await freshRepo.LoadAllAsync();

        Assert.Single(loaded);
        Assert.Equal("test-app", loaded[0].ProcessName);
        Assert.Equal(ApplicationRuleType.TrayOnly, loaded[0].RuleType);
        Assert.Null(loaded[0].CustomInterval);
    }

    [Fact]
    public async Task Multiple_rules_survive_new_repository_instance()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repo = new SqliteApplicationRuleRepository(databasePath);

        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "zoom",
            RuleType = ApplicationRuleType.TrayOnly,
        });
        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "slack",
            RuleType = ApplicationRuleType.CustomInterval,
            CustomInterval = TimeSpan.FromMinutes(15),
        });

        var freshRepo = new SqliteApplicationRuleRepository(databasePath);
        IReadOnlyList<ApplicationRule> loaded = await freshRepo.LoadAllAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, r => r.ProcessName == "zoom" && r.RuleType == ApplicationRuleType.TrayOnly);
        Assert.Contains(loaded, r =>
            r.ProcessName == "slack" &&
            r.RuleType == ApplicationRuleType.CustomInterval &&
            r.CustomInterval == TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Update_existing_rule_replaces_previous_values()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repo = new SqliteApplicationRuleRepository(databasePath);

        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "test-app",
            RuleType = ApplicationRuleType.TrayOnly,
        });

        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "test-app",
            RuleType = ApplicationRuleType.Normal,
        });

        var freshRepo = new SqliteApplicationRuleRepository(databasePath);
        IReadOnlyList<ApplicationRule> loaded = await freshRepo.LoadAllAsync();

        Assert.Single(loaded);
        Assert.Equal(ApplicationRuleType.Normal, loaded[0].RuleType);
    }

    [Fact]
    public async Task Deleted_rule_does_not_survive_new_repository_instance()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repo = new SqliteApplicationRuleRepository(databasePath);

        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "to-delete",
            RuleType = ApplicationRuleType.Silent,
        });
        await repo.SaveAsync(new ApplicationRule
        {
            ProcessName = "to-keep",
            RuleType = ApplicationRuleType.TrayOnly,
        });

        await repo.DeleteAsync("to-delete");

        var freshRepo = new SqliteApplicationRuleRepository(databasePath);
        IReadOnlyList<ApplicationRule> loaded = await freshRepo.LoadAllAsync();

        Assert.Single(loaded);
        Assert.Equal("to-keep", loaded[0].ProcessName);
    }

    [Fact]
    public async Task Empty_repository_returns_empty_list()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        var repo = new SqliteApplicationRuleRepository(databasePath);

        IReadOnlyList<ApplicationRule> loaded = await repo.LoadAllAsync();

        Assert.Empty(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
