namespace TestimusPrime.Core.Models;

public sealed record SuiteSummary(
    TestSuite Suite,
    int Total,
    int Passed,
    int Failed,
    decimal PassRate);
