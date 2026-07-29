using Microsoft.Data.Sqlite;
using RestCue.Core.Activity;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.UsageEvents;
using Xunit;

namespace RestCue.Validation.Tests.Privacy;

public sealed class ProcessNameOptInTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RestCue.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Default_opt_in_false_returns_null_process_name()
    {
        var api = new FakeFullscreenWin32Api();
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Null(context.ProcessName);
    }

    [Fact]
    public void Opt_in_true_returns_non_null_process_name()
    {
        var api = new FakeFullscreenWin32Api();
        var provider = new WindowsForegroundContextProvider(true, api);

        var context = provider.GetCurrentContext();

        Assert.NotNull(context.ProcessName);
    }

    [Fact]
    public async Task Process_name_not_written_to_usage_events()
    {
        string dbPath = Path.Combine(directory, "restcue.db");
        Directory.CreateDirectory(directory);

        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await SchemaMigrator.EnsureSchemaAsync(connection);
        }

        var repo = new SqliteUsageEventRepository(dbPath);
        await repo.WriteAsync(UsageEventType.ReminderShown, DateTimeOffset.UtcNow);
        await repo.WriteAsync(UsageEventType.BreakCompleted, DateTimeOffset.UtcNow);

        await using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload FROM usage_events WHERE payload IS NOT NULL;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string payload = reader.IsDBNull(0) ? "" : reader.GetString(0);
                Assert.DoesNotContain("processName", payload, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeFullscreenWin32Api : IFullscreenWin32Api
    {
        public IntPtr GetForegroundWindow() => 0x100;
        public IntPtr GetDesktopWindow() => 0x200;
        public IntPtr GetShellWindow() => 0x300;
        public int GetWindowLong(IntPtr hWnd, int nIndex) => 0;
        public bool GetWindowRect(IntPtr hWnd, out RECT rect)
        {
            rect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            return true;
        }
        public nint MonitorFromWindow(IntPtr hwnd, uint dwFlags) => 0x400;
        public bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi)
        {
            lpmi.Size = 40;
            lpmi.MonitorRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            lpmi.WorkRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            lpmi.Flags = 1;
            return true;
        }
    }
}
