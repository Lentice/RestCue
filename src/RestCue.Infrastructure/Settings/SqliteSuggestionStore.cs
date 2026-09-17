using System.Text.Json;
using Microsoft.Data.Sqlite;
using RestCue.Core.Reminders;

namespace RestCue.Infrastructure.Settings;

public sealed class SqliteSuggestionStore : ISuggestionStore
{
    private readonly string databasePath;
    private readonly string connectionString;
    private const string DismissedKey = "dismissed_suggestions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SqliteSuggestionStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString();
    }

    public async Task<IReadOnlySet<string>> GetDismissedProcessNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadDismissedAsync(connection, cancellationToken);
    }

    private static async Task<HashSet<string>> ReadDismissedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", DismissedKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string json || string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<HashSet<string>>(json, JsonOptions) ?? [];
    }

    public async Task DismissAsync(string processName, CancellationToken cancellationToken = default)
    {
        // Read and write share one connection: dismissing a suggestion opens the
        // database once, not twice.
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var dismissed = await ReadDismissedAsync(connection, cancellationToken);
        dismissed.Add(processName);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value, updated_at_utc)
            VALUES ($key, $value, $updatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$key", DismissedKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(dismissed, JsonOptions));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
