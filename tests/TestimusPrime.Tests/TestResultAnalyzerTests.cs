using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;

namespace TestimusPrime.Tests;

public sealed class TestResultAnalyzerTests
{
    [Fact]
    public void Analyze_BuildsSuiteAndFailureSummary()
    {
        var analyzer = new TestResultAnalyzer();
        var results = new[]
        {
            new TestExecutionResult(
                "1",
                "GET /health",
                [TestSuite.Smoke, TestSuite.Regression],
                true,
                200,
                "Passed",
                null,
                new RequestLog("GET", "https://example.test/health", new Dictionary<string, string>(), null, DateTimeOffset.UtcNow),
                new ResponseLog(200, new Dictionary<string, string>(), "{}", DateTimeOffset.UtcNow, 20, null)),
            new TestExecutionResult(
                "2",
                "POST /orders",
                [TestSuite.Regression],
                false,
                500,
                "Failed",
                "boom",
                new RequestLog("POST", "https://example.test/orders", new Dictionary<string, string>(), "{}", DateTimeOffset.UtcNow),
                new ResponseLog(500, new Dictionary<string, string>(), "{}", DateTimeOffset.UtcNow, 35, null))
        };

        var summary = analyzer.Analyze(results);

        summary.Total.Should().Be(2);
        summary.Passed.Should().Be(1);
        summary.Failed.Should().Be(1);
        summary.Suites.Should().ContainSingle(suite => suite.Suite == TestSuite.Smoke && suite.Passed == 1);
        summary.FailingTests.Should().ContainSingle(test => test == "POST /orders");
        summary.SlowestTests.First().Should().StartWith("POST /orders");
    }
}
