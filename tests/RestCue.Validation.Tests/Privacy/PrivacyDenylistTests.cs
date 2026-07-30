using Microsoft.Data.Sqlite;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
using Xunit;

namespace RestCue.Validation.Tests.Privacy;

public sealed class PrivacyDenylistTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Database_schema_and_data_contain_no_denied_content()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        var baseTime = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        long id = 1;

        foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
        {
            UsageEventPayload? payload = type switch
            {
                UsageEventType.ReminderDismissed =>
                    new ReminderDismissedPayload(ReminderResult.Snoozed),
                UsageEventType.RestDebtLevelChanged =>
                    new RestDebtLevelChangedPayload(RestDebtLevel.Level0, RestDebtLevel.Level1),
                UsageEventType.ForegroundProcessChanged =>
                    new ForegroundProcessChangedPayload("test-app"),
                UsageEventType.ErrorOccurred =>
                    new ErrorOccurredPayload("TestError"),
                _ => null
            };
            await repo.WriteAsync(type, baseTime.AddSeconds(id), payload);
            id++;
        }

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();

            await using var schemaCmd = connection.CreateCommand();
            schemaCmd.CommandText = "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL;";
            await using var schemaReader = await schemaCmd.ExecuteReaderAsync();
            while (await schemaReader.ReadAsync())
            {
                string sql = schemaReader.GetString(0);
                Assert.False(PrivacyDenylist.ContainsDeniedContent(sql),
                    $"Schema contains denied content: {sql}");
            }

            await using var dataCmd = connection.CreateCommand();
            dataCmd.CommandText = "SELECT id, occurred_utc, event_type, payload FROM usage_events;";
            await using var dataReader = await dataCmd.ExecuteReaderAsync();
            while (await dataReader.ReadAsync())
            {
                for (int i = 0; i < dataReader.FieldCount; i++)
                {
                    if (!dataReader.IsDBNull(i))
                    {
                        string value = dataReader.GetString(i);
                        Assert.False(PrivacyDenylist.ContainsDeniedContent(value),
                            $"Column '{dataReader.GetName(i)}' contains denied content: {value}");
                    }
                }

                string payload = dataReader.IsDBNull(3) ? "" : dataReader.GetString(3);
                if (!string.IsNullOrEmpty(payload))
                {
                    Assert.DoesNotContain("windowTitle", payload, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("clipboard", payload, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("url", payload, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("documentName", payload, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("screenContent", payload, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("path", payload, StringComparison.OrdinalIgnoreCase);

                    Assert.True(
                        payload.Contains("\"result\"", StringComparison.OrdinalIgnoreCase) ||
                        (payload.Contains("\"previous\"", StringComparison.OrdinalIgnoreCase) &&
                         payload.Contains("\"current\"", StringComparison.OrdinalIgnoreCase)) ||
                        payload.Contains("\"processName\"", StringComparison.OrdinalIgnoreCase) ||
                        payload.Contains("\"errorCategory\"", StringComparison.OrdinalIgnoreCase),
                        "Payload must contain only allowed keys");
                }
            }
        }

        string[] simulatedLogMessages =
        [
            "RestCue: usage event channel full; event dropped.",
            "RestCue: failed to persist usage event.",
        ];

        foreach (var log in simulatedLogMessages)
        {
            Assert.False(PrivacyDenylist.ContainsDeniedContent(log),
                $"Log message contains denied content: {log}");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
