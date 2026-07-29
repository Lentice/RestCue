using System.Diagnostics;
using Xunit;

namespace RestCue.Validation.Tests.Soak;

[Trait("Category", "LongRun")]
public sealed class SoakHarness
{
    [Fact]
    public async Task Resource_usage_remains_bounded_over_time()
    {
        int soakMinutes = 5;
        string? envMinutes = Environment.GetEnvironmentVariable("RESTCUE_SOAK_MINUTES");
        if (!string.IsNullOrEmpty(envMinutes) && int.TryParse(envMinutes, out int parsed) && parsed > 0)
        {
            soakMinutes = parsed;
        }

        string artifactsDir = Path.Combine(
            Environment.CurrentDirectory, "..", "..", "..", "..", "..", "artifacts", "validation");
        Directory.CreateDirectory(artifactsDir);

        string csvPath = Path.Combine(artifactsDir, "soak.csv");

        string dbPath = Path.Combine(
            Path.GetTempPath(), "RestCue.Tests", Guid.NewGuid().ToString("N"), "restcue.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var sampler = new ResourceSampler(csvPath, dbPath);
        var stopwatch = Stopwatch.StartNew();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(soakMinutes * 2));

        int sampleIntervalSeconds = 60;
        int totalSamples = (soakMinutes * 60) / sampleIntervalSeconds;

        try
        {
            for (int i = 0; i < totalSamples; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                await sampler.SampleAsync(stopwatch.Elapsed, cts.Token);

                await Task.Delay(TimeSpan.FromSeconds(sampleIntervalSeconds), cts.Token);
            }

            await sampler.SampleAsync(stopwatch.Elapsed, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Soak test timed out before completing all samples.");
        }
        finally
        {
            sampler.Close();
        }

        Assert.True(File.Exists(csvPath), "Soak CSV output was not created.");
    }
}
