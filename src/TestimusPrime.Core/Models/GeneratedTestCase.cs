namespace TestimusPrime.Core.Models;

public sealed record GeneratedTestCase(
    string Id,
    string Name,
    ApiEndpoint Endpoint,
    IReadOnlyList<TestSuite> Suites,
    string RelativePath,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    IReadOnlyList<int> ExpectedStatusCodes,
    bool AllowAnyNonServerError,
    string Rationale);
