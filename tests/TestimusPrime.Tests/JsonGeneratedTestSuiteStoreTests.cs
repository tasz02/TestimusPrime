using TestimusPrime.Core.Models;
using TestimusPrime.Core.Storage;

namespace TestimusPrime.Tests;

public sealed class JsonGeneratedTestSuiteStoreTests
{
    [Fact]
    public void CreateDefaultVersionTag_ReturnsUniqueValues()
    {
        var first = JsonGeneratedTestSuiteStore.CreateDefaultVersionTag();
        var second = JsonGeneratedTestSuiteStore.CreateDefaultVersionTag();

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsGeneratedSuite()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"testimusprime-generated-suite-{Guid.NewGuid():n}");
        var store = new JsonGeneratedTestSuiteStore(outputDirectory);
        var suite = new GeneratedTestSuite(
            "testcases-v20260927150000",
            "/repo",
            DateTimeOffset.UtcNow,
            new RepositoryAnalysis(
                "/repo",
                DateTimeOffset.UtcNow,
                [new ApiEndpoint("1", "GET", "/status", EndpointSourceType.MinimalApi, "/repo/Program.cs", 10)],
                []),
            [
                new GeneratedTestCase(
                    "test-1",
                    "GET /status",
                    new ApiEndpoint("1", "GET", "/status", EndpointSourceType.MinimalApi, "/repo/Program.cs", 10),
                    [TestSuite.Smoke, TestSuite.Regression],
                    "/status",
                    new Dictionary<string, string> { ["Accept"] = "application/json" },
                    null,
                    [200],
                    true,
                    "health check")
            ]);

        try
        {
            var suitePath = await store.SaveAsync(suite);
            var loaded = await JsonGeneratedTestSuiteStore.LoadAsync(suitePath);

            loaded.VersionTag.Should().Be(suite.VersionTag);
            loaded.RepositoryPath.Should().Be(suite.RepositoryPath);
            loaded.Analysis.Endpoints.Should().ContainSingle(endpoint => endpoint.Route == "/status");
            loaded.TestCases.Should().ContainSingle(testCase => testCase.RelativePath == "/status");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
        }
    }
}
