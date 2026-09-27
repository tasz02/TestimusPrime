namespace TestimusPrime.Core.Models;

public sealed record RequestLog(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    DateTimeOffset SentAt);
