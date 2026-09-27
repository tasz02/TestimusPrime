namespace TestimusPrime.Core.Models;

public sealed record ExecutionSelection(
    TriggerEvent TriggerEvent,
    IReadOnlyList<TestSuite> Suites,
    IReadOnlyList<GeneratedTestCase> TestCases);
