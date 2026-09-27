namespace TestimusPrime.Core.Models;

public sealed record GeneratedTestSuite(
    string VersionTag,
    string RepositoryPath,
    DateTimeOffset GeneratedAt,
    RepositoryAnalysis Analysis,
    IReadOnlyList<GeneratedTestCase> TestCases);
