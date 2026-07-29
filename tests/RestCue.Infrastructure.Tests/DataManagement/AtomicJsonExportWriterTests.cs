using RestCue.Infrastructure.DataManagement;
using Xunit;

namespace RestCue.Infrastructure.Tests.DataManagement;

public sealed class AtomicJsonExportWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_and_commit_creates_file_at_destination()
    {
        string destPath = Path.Combine(directory, "export.json");
        Directory.CreateDirectory(directory);

        using (var writer = new AtomicJsonExportWriter(destPath))
        {
            await writer.WriteAsync("{\"key\":\"value\"}");
            await writer.CommitAsync();
        }

        Assert.True(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".tmp"));
        Assert.Equal("{\"key\":\"value\"}", await File.ReadAllTextAsync(destPath));
    }

    [Fact]
    public async Task Overwrites_existing_file_atomically()
    {
        string destPath = Path.Combine(directory, "export.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(destPath, "old content");

        using (var writer = new AtomicJsonExportWriter(destPath))
        {
            await writer.WriteAsync("new content");
            await writer.CommitAsync();
        }

        Assert.Equal("new content", await File.ReadAllTextAsync(destPath));
        Assert.False(File.Exists(destPath + ".tmp"));
    }

    [Fact]
    public async Task Dispose_without_commit_removes_temp_file()
    {
        string destPath = Path.Combine(directory, "export.json");
        Directory.CreateDirectory(directory);

        var writer = new AtomicJsonExportWriter(destPath);
        await writer.WriteAsync("partial data");
        writer.Dispose();

        Assert.False(File.Exists(destPath + ".tmp"));
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task Dispose_after_commit_keeps_destination()
    {
        string destPath = Path.Combine(directory, "export.json");
        Directory.CreateDirectory(directory);

        var writer = new AtomicJsonExportWriter(destPath);
        await writer.WriteAsync("data");
        await writer.CommitAsync();
        writer.Dispose();

        Assert.True(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
