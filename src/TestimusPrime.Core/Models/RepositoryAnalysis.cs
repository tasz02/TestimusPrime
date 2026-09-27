namespace TestimusPrime.Core.Models;

public sealed record RepositoryAnalysis(
    string RepositoryPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<ApiEndpoint> Endpoints,
    IReadOnlyList<string> Notes);
