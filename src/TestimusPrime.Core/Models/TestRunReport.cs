namespace TestimusPrime.Core.Models;

public sealed record TestRunReport(
    string RunId,
    string RepositoryPath,
    Uri ApiBaseUrl,
    TriggerEvent TriggerEvent,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    RepositoryAnalysis Analysis,
    IReadOnlyList<GeneratedTestCase> GeneratedTestCases,
    IReadOnlyList<TestExecutionResult> Results,
    TestRunSummary Summary);
