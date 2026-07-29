namespace RestCue.Core.DataManagement;

public interface IExportWriter : IDisposable
{
    Task WriteAsync(string json, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}
