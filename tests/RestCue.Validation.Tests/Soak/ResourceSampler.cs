using System.Diagnostics;

namespace RestCue.Validation.Tests.Soak;

public sealed class ResourceSampler : IDisposable
{
    private readonly Process process;
    private readonly string dbPath;
    private readonly StreamWriter writer;
    private bool disposed;
    private int sampleIndex;

    public ResourceSampler(string outputPath, string dbPath)
    {
        process = Process.GetCurrentProcess();
        this.dbPath = dbPath;
        writer = OpenArtifactWriter(outputPath);
        writer.WriteLine("Sample,ElapsedSeconds,TotalProcessorSeconds,WorkingSetMB,PrivateMemoryMB,HandleCount,ThreadCount,DatabaseFileKB");
    }

    public async Task SampleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
    {
        process.Refresh();

        long dbBytes = 0;
        if (File.Exists(dbPath))
        {
            try { dbBytes = new FileInfo(dbPath).Length; }
            catch { }
        }

        await writer.WriteLineAsync(
            $"{sampleIndex}," +
            $"{elapsed.TotalSeconds:F0}," +
            $"{process.TotalProcessorTime.TotalSeconds:F3}," +
            $"{process.WorkingSet64 / (1024.0 * 1024.0):F2}," +
            $"{process.PrivateMemorySize64 / (1024.0 * 1024.0):F2}," +
            $"{process.HandleCount}," +
            $"{process.Threads.Count}," +
            $"{dbBytes / 1024.0:F2}");

        sampleIndex++;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        writer.Dispose();
        disposed = true;
    }

    private static StreamWriter OpenArtifactWriter(string outputPath)
    {
        try
        {
            return CreateWriter(outputPath);
        }
        catch (IOException)
        {
            string directory = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException("The soak artifact path must include a directory.");
            string fileName = Path.GetFileNameWithoutExtension(outputPath);
            string extension = Path.GetExtension(outputPath);
            string isolatedPath = Path.Combine(
                directory,
                $"{fileName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}{extension}");

            return CreateWriter(isolatedPath);
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        return new StreamWriter(stream);
    }
}
