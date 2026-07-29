using RestCue.Core.DataManagement;

namespace RestCue.Infrastructure.DataManagement;

public sealed class AtomicJsonExportWriter : IExportWriter
{
    private readonly string tempPath;
    private readonly string finalPath;
    private readonly FileStream stream;
    private bool committed;

    public AtomicJsonExportWriter(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string directory = Path.GetDirectoryName(destinationPath)!;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        tempPath = destinationPath + ".tmp";
        finalPath = destinationPath;
        stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
    {
        byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(json);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await stream.FlushAsync(cancellationToken);
        stream.Close();
        committed = true;

        File.Move(tempPath, finalPath, overwrite: true);
    }

    public void Dispose()
    {
        stream.Dispose();
        if (!committed && File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}
