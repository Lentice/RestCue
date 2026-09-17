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

    private static readonly Dictionary<UsageEventType, Type> PayloadTypeByEventType =
        new()
        {
            [UsageEventType.ReminderDismissed] = typeof(ReminderDismissedPayload),
            [UsageEventType.RestDebtLevelChanged] = typeof(RestDebtLevelChangedPayload),
            [UsageEventType.ForegroundProcessChanged] = typeof(ForegroundProcessChangedPayload),
            [UsageEventType.ErrorOccurred] = typeof(ErrorOccurredPayload),
        };

    public Task WriteAsync(
        UsageEventType eventType,
        DateTimeOffset occurredUtc,
        UsageEventPayload? payload = null,
        CancellationToken cancellationToken = default) =>
        WriteManyAsync([new UsageEventWrite(eventType, occurredUtc, payload)], cancellationToken);

    public async Task WriteManyAsync(
        IReadOnlyList<UsageEventWrite> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return;

        foreach (var e in events)
            Validate(e.EventType, e.Payload);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ApplyWritePragmasAsync(connection, cancellationToken);

        // One transaction for the whole batch: the rollback journal (or WAL) is
        // written and synced once instead of once per event.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO usage_events (occurred_utc, event_type, payload)
            VALUES ($occurredUtc, $eventType, $payload);
            """;
        var occurredParam = command.Parameters.Add("$occurredUtc", SqliteType.Text);
        var typeParam = command.Parameters.Add("$eventType", SqliteType.Text);
        var payloadParam = command.Parameters.Add("$payload", SqliteType.Text);

        foreach (var e in events)
        {
            occurredParam.Value = e.OccurredUtc.ToUniversalTime().ToString("O");
            typeParam.Value = e.EventType.ToString();
            payloadParam.Value = SerializePayload(e.Payload);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void Validate(UsageEventType eventType, UsageEventPayload? payload)
    {
        if (payload != null && !PayloadTypeByEventType.ContainsKey(eventType))
            throw new ArgumentException(
                $"Event type {eventType} does not accept a payload.", nameof(payload));
        if (payload == null && PayloadTypeByEventType.ContainsKey(eventType))
            throw new ArgumentException(
                $"Event type {eventType} requires a payload.", nameof(payload));
        if (payload != null && payload.GetType() != PayloadTypeByEventType[eventType])
            throw new ArgumentException(
                $"Event type {eventType} expects a {PayloadTypeByEventType[eventType].Name} payload.",
                nameof(payload));
    }

    /// <summary>
    /// The journal mode is a persistent database property set during migration; the sync
    /// level is per connection, so the write path sets it on every connection it opens.
    /// NORMAL keeps commits out of fsync in WAL mode: a crash can cost the most recent
    /// events, never the database.
    /// </summary>
    private static async Task ApplyWritePragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous=NORMAL;";
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

        if (!PayloadTypeByEventType.ContainsKey(eventType))
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
