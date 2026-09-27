namespace TestimusPrime.Core.Models;

public sealed record TestRunSummary(
    int Total,
    int Passed,
    int Failed,
    decimal PassRate,
    IReadOnlyList<SuiteSummary> Suites,
    IReadOnlyDictionary<string, int> StatusCodeDistribution,
    IReadOnlyList<string> FailingTests,
    IReadOnlyList<string> SlowestTests);
