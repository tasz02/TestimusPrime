using TestimusPrime.Core.Services;

namespace TestimusPrime.Tests;

public sealed class RepositoryAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_FindsControllerAndMinimalApiEndpoints()
    {
        var repositoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixtures/SampleApi"));
        var analyzer = new RepositoryAnalyzer();

        var analysis = await analyzer.AnalyzeAsync(repositoryPath);

        analysis.Endpoints.Should().ContainSingle(endpoint => endpoint.HttpMethod == "GET" && endpoint.Route == "/api/Orders/health");
        analysis.Endpoints.Should().ContainSingle(endpoint => endpoint.HttpMethod == "POST" && endpoint.Route == "/api/Orders");
        analysis.Endpoints.Should().ContainSingle(endpoint => endpoint.HttpMethod == "GET" && endpoint.Route == "/status");
    }
}
