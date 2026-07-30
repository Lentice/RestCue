using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;

namespace RestCue.Infrastructure.UsageEvents;

public sealed class SqliteUsageEventRepository : IUsageEventRepository
{
    private readonly string connectionString;

    public SqliteUsageEventRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString();
    }

    private static readonly HashSet<UsageEventType> PayloadEventTypes =
    [
        UsageEventType.ReminderDismissed,
        UsageEventType.RestDebtLevelChanged,
        UsageEventType.ForegroundProcessChanged,
        UsageEventType.ErrorOccurred,
    ];

    public async Task WriteAsync(
        UsageEventType eventType,
        DateTimeOffset occurredUtc,
        UsageEventPayload? payload = null,
        CancellationToken cancellationToken = default)
    {
        if (payload != null && !PayloadEventTypes.Contains(eventType))
            throw new ArgumentException(
                $"Event type {eventType} does not accept a payload.", nameof(payload));
        if (payload == null && PayloadEventTypes.Contains(eventType))
            throw new ArgumentException(
                $"Event type {eventType} requires a payload.", nameof(payload));

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO usage_events (occurred_utc, event_type, payload)
            VALUES ($occurredUtc, $eventType, $payload);
            """;
        command.Parameters.AddWithValue("$occurredUtc", occurredUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$eventType", eventType.ToString());
        command.Parameters.AddWithValue("$payload", SerializePayload(payload));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageEvent>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, occurred_utc, event_type, payload
            FROM usage_events
            WHERE occurred_utc >= $from AND occurred_utc <= $to
            ORDER BY occurred_utc, id;
            """;
        command.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("O"));

        var results = new List<UsageEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            var occurredUtc = DateTimeOffset.Parse(
                reader.GetString(1), null, DateTimeStyles.RoundtripKind).ToUniversalTime();
            var eventType = Enum.Parse<UsageEventType>(reader.GetString(2));
            UsageEventPayload? payload = reader.IsDBNull(3)
                ? null
                : DeserializePayload(eventType, reader.GetString(3));
            results.Add(new UsageEvent(id, occurredUtc, eventType, payload));
        }

        return results;
    }

    private static object? SerializePayload(UsageEventPayload? payload)
    {
        if (payload == null)
            return DBNull.Value;

        return payload switch
        {
            ReminderDismissedPayload p => JsonSerializer.Serialize(
                new { result = p.Result.ToString() }),
            RestDebtLevelChangedPayload p => JsonSerializer.Serialize(
                new { previous = p.Previous.ToString(), current = p.Current.ToString() }),
            ForegroundProcessChangedPayload p => JsonSerializer.Serialize(
                new { processName = p.ProcessName }),
            ErrorOccurredPayload p => JsonSerializer.Serialize(
                new { errorCategory = p.ErrorCategory }),
            _ => DBNull.Value
        };
    }

    private static UsageEventPayload? DeserializePayload(UsageEventType eventType, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!PayloadEventTypes.Contains(eventType))
            throw new InvalidOperationException(
                $"Event type {eventType} is not expected to have a stored payload.");

        return eventType switch
        {
            UsageEventType.ReminderDismissed =>
                new ReminderDismissedPayload(
                    Enum.Parse<ReminderResult>(root.GetProperty("result").GetString()!)),
            UsageEventType.RestDebtLevelChanged =>
                new RestDebtLevelChangedPayload(
                    Enum.Parse<RestDebtLevel>(root.GetProperty("previous").GetString()!),
                    Enum.Parse<RestDebtLevel>(root.GetProperty("current").GetString()!)),
            UsageEventType.ForegroundProcessChanged =>
                new ForegroundProcessChangedPayload(
                    root.GetProperty("processName").GetString()!),
            UsageEventType.ErrorOccurred =>
                new ErrorOccurredPayload(
                    root.GetProperty("errorCategory").GetString()!),
            _ => throw new InvalidOperationException(
                $"Unexpected payload-bearing event type: {eventType}.")
        };
    }
}
