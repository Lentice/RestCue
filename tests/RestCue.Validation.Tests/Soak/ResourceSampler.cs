using System.Diagnostics;

namespace RestCue.Validation.Tests.Soak;

public sealed class ResourceSampler
{
    private readonly Process process;
    private readonly string dbPath;
    private readonly StreamWriter writer;
    private int sampleIndex;

    public ResourceSampler(string outputPath, string dbPath)
    {
        this.process = Process.GetCurrentProcess();
        this.dbPath = dbPath;
        writer = new StreamWriter(outputPath, append: false);
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

    public void Close()
    {
        writer.Dispose();
    }
}
