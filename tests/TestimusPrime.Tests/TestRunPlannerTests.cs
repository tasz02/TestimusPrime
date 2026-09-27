using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;

namespace TestimusPrime.Tests;

public sealed class TestRunPlannerTests
{
    [Fact]
    public void Plan_ForCommit_SelectsOnlySmokeCoverage()
    {
        var planner = new TestRunPlanner();
        var testCases = CreateTestCases();

        var selection = planner.Plan(testCases, TriggerEvent.Commit);

        selection.Suites.Should().BeEquivalentTo([TestSuite.Smoke]);
        selection.TestCases.Should().ContainSingle(testCase => testCase.Id == "smoke");
    }

    [Fact]
    public void Plan_ForPullRequest_SelectsRegressionCoverage()
    {
        var planner = new TestRunPlanner();
        var testCases = CreateTestCases();

        var selection = planner.Plan(testCases, TriggerEvent.PullRequest);

        selection.Suites.Should().BeEquivalentTo([TestSuite.Regression]);
        selection.TestCases.Should().HaveCount(2);
    }

    private static GeneratedTestCase[] CreateTestCases() =>
    [
        new(
            "smoke",
            "GET /health",
            new ApiEndpoint("1", "GET", "/health", EndpointSourceType.MinimalApi, "/repo/Program.cs", 10),
            [TestSuite.Smoke, TestSuite.Regression],
            "/health",
            new Dictionary<string, string>(),
            null,
            [200],
            true,
            "smoke"),
        new(
            "regression",
            "POST /orders",
            new ApiEndpoint("2", "POST", "/orders", EndpointSourceType.Controller, "/repo/OrdersController.cs", 20),
            [TestSuite.Regression],
            "/orders",
            new Dictionary<string, string>(),
            "{}",
            [201],
            false,
            "regression")
    ];
}
