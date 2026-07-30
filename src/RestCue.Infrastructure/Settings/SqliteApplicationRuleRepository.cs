using System.Globalization;
using Microsoft.Data.Sqlite;
using RestCue.Core.Reminders;

namespace RestCue.Infrastructure.Settings;

public sealed class SqliteApplicationRuleRepository : IApplicationRuleRepository
{
    private readonly string databasePath;
    private readonly string connectionString;

    public SqliteApplicationRuleRepository(string databasePath)
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

    public async Task<IReadOnlyList<ApplicationRule>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectoryExists();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT process_name, rule_type, custom_interval_seconds FROM application_rules ORDER BY process_name;";

        var rules = new List<ApplicationRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string processName = reader.GetString(0);
            string ruleTypeStr = reader.GetString(1);
            var ruleType = Enum.Parse<ApplicationRuleType>(ruleTypeStr);

            TimeSpan? customInterval = null;
            if (!reader.IsDBNull(2))
            {
                customInterval = TimeSpan.FromSeconds(reader.GetInt64(2));
            }

            rules.Add(new ApplicationRule
            {
                ProcessName = processName,
                RuleType = ruleType,
                CustomInterval = customInterval,
            });
        }

        return rules;
    }

    public async Task SaveAsync(ApplicationRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.ProcessName);

        EnsureDirectoryExists();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO application_rules (process_name, rule_type, custom_interval_seconds, updated_at_utc)
            VALUES ($processName, $ruleType, $customIntervalSeconds, $updatedAtUtc)
            ON CONFLICT(process_name) DO UPDATE SET
                rule_type = excluded.rule_type,
                custom_interval_seconds = excluded.custom_interval_seconds,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$processName", rule.ProcessName);
        command.Parameters.AddWithValue("$ruleType", rule.RuleType.ToString());
        command.Parameters.AddWithValue("$customIntervalSeconds", (object?)rule.CustomInterval?.TotalSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string processName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        EnsureDirectoryExists();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SchemaMigrator.EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM application_rules WHERE process_name = $processName;";
        command.Parameters.AddWithValue("$processName", processName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureDirectoryExists()
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
