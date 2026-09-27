namespace TestimusPrime.Core.Models;

public sealed record DashboardSummary(
    int TotalRuns,
    int TotalTests,
    int PassedTests,
    int FailedTests,
    decimal AveragePassRate,
    IReadOnlyList<RunSummaryCard> RecentRuns);

public sealed record RunSummaryCard(
    string RunId,
    TriggerEvent TriggerEvent,
    DateTimeOffset CompletedAt,
    int Total,
    int Passed,
    int Failed,
    decimal PassRate);
