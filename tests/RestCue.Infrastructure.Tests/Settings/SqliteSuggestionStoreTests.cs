using Microsoft.Data.Sqlite;
using RestCue.Infrastructure.Settings;
using Xunit;

namespace RestCue.Infrastructure.Tests.Settings;

public sealed class SqliteSuggestionStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    public SqliteSuggestionStoreTests()
    {
        Directory.CreateDirectory(directory);
    }

    private async Task CreateSchemaAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await SchemaMigrator.EnsureSchemaAsync(connection);
    }

    [Fact]
    public async Task Dismissed_name_survives_new_store_instance()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        await CreateSchemaAsync(databasePath);
        var store = new SqliteSuggestionStore(databasePath);

        await store.DismissAsync("vlc");

        var freshStore = new SqliteSuggestionStore(databasePath);
        IReadOnlySet<string> dismissed = await freshStore.GetDismissedProcessNamesAsync();

        Assert.Equal(new HashSet<string> { "vlc" }, dismissed);
    }

    [Fact]
    public async Task Empty_store_returns_empty_set()
    {
        string databasePath = Path.Combine(directory, "restcue.db");
        await CreateSchemaAsync(databasePath);
        var store = new SqliteSuggestionStore(databasePath);

        IReadOnlySet<string> dismissed = await store.GetDismissedProcessNamesAsync();

        Assert.Empty(dismissed);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
