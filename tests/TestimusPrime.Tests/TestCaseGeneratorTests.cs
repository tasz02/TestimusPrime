using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;

namespace TestimusPrime.Tests;

public sealed class TestCaseGeneratorTests
{
    [Fact]
    public void Generate_AssignsSuitesAndAddsNegativeCoverage()
    {
        var analysis = new RepositoryAnalysis(
            "/repo",
            DateTimeOffset.UtcNow,
            [
                new ApiEndpoint("1", "GET", "/health", EndpointSourceType.MinimalApi, "/repo/Program.cs", 10),
                new ApiEndpoint("2", "POST", "/orders", EndpointSourceType.Controller, "/repo/OrdersController.cs", 20),
                new ApiEndpoint("3", "GET", "/orders/{id:int}", EndpointSourceType.Controller, "/repo/OrdersController.cs", 30),
                new ApiEndpoint("4", "GET", "/orders/{orderId:guid}", EndpointSourceType.Controller, "/repo/OrdersController.cs", 40)
            ],
            []);

        var generator = new TestCaseGenerator();

        var testCases = generator.Generate(analysis);
        var invalidBody = testCases.Single(test => test.Id == "test:2:invalid-body");
        var invalidRoute = testCases.Single(test => test.Id == "test:3:invalid-route");
        var invalidGuidRoute = testCases.Single(test => test.Id == "test:4:invalid-route");

        testCases.Single(test => test.Endpoint.Route == "/health").Suites.Should().BeEquivalentTo([TestSuite.Smoke, TestSuite.Regression]);
        testCases.Single(test => test.Id == "test:2").Suites.Should().BeEquivalentTo([TestSuite.Regression]);
        invalidBody.Body.Should().Be("{");
        invalidBody.ExpectedStatusCodes.Should().Equal([400]);
        invalidBody.Suites.Should().Equal([TestSuite.Regression]);
        invalidRoute.RelativePath.Should().Be("/orders/invalid");
        invalidRoute.ExpectedStatusCodes.Should().Equal([400, 404]);
        invalidRoute.Suites.Should().Equal([TestSuite.Regression]);
        invalidGuidRoute.RelativePath.Should().Be("/orders/not-a-guid");
        invalidGuidRoute.ExpectedStatusCodes.Should().Equal([400, 404]);
        invalidGuidRoute.Suites.Should().Equal([TestSuite.Regression]);
    }
}
