namespace RestCue.Core.Transparency;

public interface IDataTransparencyService
{
    Task<DataTransparencySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
