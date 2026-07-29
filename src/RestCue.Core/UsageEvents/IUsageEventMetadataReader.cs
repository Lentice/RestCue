namespace RestCue.Core.UsageEvents;

public interface IUsageEventMetadataReader
{
    Task<UsageEventMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default);
}
