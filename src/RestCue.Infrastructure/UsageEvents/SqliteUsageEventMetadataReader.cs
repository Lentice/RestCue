using System.Globalization;
using Microsoft.Data.Sqlite;
using RestCue.Core.UsageEvents;

namespace RestCue.Infrastructure.UsageEvents;

public sealed class SqliteUsageEventMetadataReader : IUsageEventMetadataReader
{
    private readonly string connectionString;

    public SqliteUsageEventMetadataReader(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString();
    }

    public async Task<UsageEventMetadata> ReadMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var totalCount = 0L;
        DateTimeOffset? earliestUtc = null;
        DateTimeOffset? latestUtc = null;
        var perTypeRaw = new Dictionary<string, long>();
        var unparsableRowCount = 0L;
        var schemaVersion = 0L;

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM usage_events;";
            totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        if (totalCount > 0)
        {
            await using (var rangeCommand = connection.CreateCommand())
            {
                rangeCommand.CommandText =
                    """
                    SELECT MIN(occurred_utc), MAX(occurred_utc) FROM usage_events;
                    """;
                await using var reader = await rangeCommand.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        var minStr = reader.GetString(0);
                        try
                        {
                            earliestUtc = DateTimeOffset.Parse(minStr, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
                        }
                        catch
                        {
                            unparsableRowCount++;
                        }
                    }
                    if (!reader.IsDBNull(1))
                    {
                        var maxStr = reader.GetString(1);
                        try
                        {
                            latestUtc = DateTimeOffset.Parse(maxStr, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
                        }
                        catch
                        {
                            unparsableRowCount++;
                        }
                    }
                }
            }
        }

        await using (var typeCommand = connection.CreateCommand())
        {
            typeCommand.CommandText =
                """
                SELECT event_type, COUNT(*) AS cnt
                FROM usage_events
                GROUP BY event_type;
                """;
            await using var reader = await typeCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var typeStr = reader.GetString(0);
                var count = reader.GetInt64(1);
                perTypeRaw[typeStr] = count;
            }
        }

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "PRAGMA user_version;";
            schemaVersion = (long)(await versionCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var perTypeCounts = new Dictionary<UsageEventType, long>();
        foreach (var (typeStr, count) in perTypeRaw)
        {
            if (Enum.TryParse<UsageEventType>(typeStr, out var eventType))
            {
                perTypeCounts[eventType] = count;
            }
            else
            {
                unparsableRowCount += count;
            }
        }

        return new UsageEventMetadata(
            totalCount,
            earliestUtc,
            latestUtc,
            perTypeCounts,
            unparsableRowCount,
            schemaVersion);
    }
}
