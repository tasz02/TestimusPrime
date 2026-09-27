using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;

namespace TestimusPrime.Tests;

public sealed class TestCaseGeneratorTests
{
    [Fact]
    public void Generate_AssignsSmokeAndRegressionSuites()
    {
        var analysis = new RepositoryAnalysis(
            "/repo",
            DateTimeOffset.UtcNow,
            [
                new ApiEndpoint("1", "GET", "/health", EndpointSourceType.MinimalApi, "/repo/Program.cs", 10),
                new ApiEndpoint("2", "POST", "/orders", EndpointSourceType.Controller, "/repo/OrdersController.cs", 20)
            ],
            []);

        var generator = new TestCaseGenerator();

        var testCases = generator.Generate(analysis);

        testCases.Single(test => test.Endpoint.Route == "/health").Suites.Should().BeEquivalentTo([TestSuite.Smoke, TestSuite.Regression]);
        testCases.Single(test => test.Endpoint.Route == "/orders").Suites.Should().BeEquivalentTo([TestSuite.Regression]);
    }
}
