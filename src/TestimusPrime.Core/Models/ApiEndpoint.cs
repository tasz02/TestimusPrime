namespace TestimusPrime.Core.Models;

public sealed record ApiEndpoint(
    string Id,
    string HttpMethod,
    string Route,
    EndpointSourceType SourceType,
    string SourceFile,
    int SourceLine,
    string? GroupName = null,
    string? ActionName = null);
