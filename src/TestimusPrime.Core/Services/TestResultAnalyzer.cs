using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed class TestResultAnalyzer
{
    public TestRunSummary Analyze(IReadOnlyList<TestExecutionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var total = results.Count;
        var passed = results.Count(static result => result.Passed);
        var failed = total - passed;
        var passRate = total == 0 ? 0 : Math.Round((decimal)passed / total * 100, 2);

        var suiteSummaries = results
            .SelectMany(result => result.Suites.Select(suite => (suite, result.Passed)))
            .GroupBy(static item => item.suite)
            .Select(group =>
            {
                var suiteTotal = group.Count();
                var suitePassed = group.Count(static item => item.Passed);
                var suiteFailed = suiteTotal - suitePassed;
                var suitePassRate = suiteTotal == 0 ? 0 : Math.Round((decimal)suitePassed / suiteTotal * 100, 2);
                return new SuiteSummary(group.Key, suiteTotal, suitePassed, suiteFailed, suitePassRate);
            })
            .OrderBy(static summary => summary.Suite)
            .ToArray();

        var statusCodes = results
            .GroupBy(result => result.StatusCode?.ToString() ?? "error")
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var failingTests = results
            .Where(static result => !result.Passed)
            .Select(static result => result.TestName)
            .ToArray();

        var slowestTests = results
            .OrderByDescending(static result => result.Response.DurationMilliseconds)
            .Take(5)
            .Select(static result => $"{result.TestName} ({result.Response.DurationMilliseconds} ms)")
            .ToArray();

        return new TestRunSummary(total, passed, failed, passRate, suiteSummaries, statusCodes, failingTests, slowestTests);
    }
}
