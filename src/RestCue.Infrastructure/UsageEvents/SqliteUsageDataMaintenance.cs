using Microsoft.Data.Sqlite;
using RestCue.Core.DataManagement;

namespace RestCue.Infrastructure.UsageEvents;

public sealed class SqliteUsageDataMaintenance : IUsageDataMaintenance
{
    private readonly string connectionString;

    public SqliteUsageDataMaintenance(string databasePath)
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

    public async Task<ClearResult> ClearUsageHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                command.CommandText = "DELETE FROM usage_events;";
                int affected = await command.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return new ClearResult(true, affected, null);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            return new ClearResult(false, 0, ex.Message);
        }
    }
}
