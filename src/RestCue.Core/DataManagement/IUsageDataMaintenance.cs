namespace RestCue.Core.DataManagement;

public interface IUsageDataMaintenance
{
    Task<ClearResult> ClearUsageHistoryAsync(CancellationToken cancellationToken = default);
}
