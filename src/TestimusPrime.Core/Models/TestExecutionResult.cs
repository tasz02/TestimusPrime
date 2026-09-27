namespace TestimusPrime.Core.Models;

public sealed record TestExecutionResult(
    string TestCaseId,
    string TestName,
    IReadOnlyList<TestSuite> Suites,
    bool Passed,
    int? StatusCode,
    string Outcome,
    string? FailureReason,
    RequestLog Request,
    ResponseLog Response);
