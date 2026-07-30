using Xunit;

namespace RestCue.Validation.Tests.Soak;

public sealed class ResourceSamplerTests
{
    [Fact]
    public void Concurrent_samplers_do_not_contend_for_the_same_artifact()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "RestCue.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string outputPath = Path.Combine(directory, "soak.csv");
        string dbPath = Path.Combine(directory, "restcue.db");

        ResourceSampler? first = null;
        ResourceSampler? second = null;
        try
        {
            first = new ResourceSampler(outputPath, dbPath);
            second = new ResourceSampler(outputPath, dbPath);

            Assert.Equal(2, Directory.GetFiles(directory, "soak*.csv").Length);
        }
        finally
        {
            second?.Close();
            first?.Close();
            Directory.Delete(directory, recursive: true);
        }
    }
}
