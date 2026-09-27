namespace TestimusPrime.Core.Models;

public sealed record ResponseLog(
    int? StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    DateTimeOffset ReceivedAt,
    long DurationMilliseconds,
    string? Error);
