namespace RestCue.Core.DataManagement;

public interface IUsageDataExporter
{
    Task<ExportResult> ExportAsync(string destinationPath, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
