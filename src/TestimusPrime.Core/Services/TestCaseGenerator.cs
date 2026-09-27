using System.Text.RegularExpressions;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed partial class TestCaseGenerator
{
    private static readonly Regex RouteTokenRegex = RouteTokenPattern();

    public IReadOnlyList<GeneratedTestCase> Generate(RepositoryAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return analysis.Endpoints
            .Select(endpoint =>
            {
                var smokeCandidate = IsSmokeCandidate(endpoint);
                var suites = smokeCandidate
                    ? new[] { TestSuite.Smoke, TestSuite.Regression }
                    : new[] { TestSuite.Regression };

                var relativePath = ResolveRoute(endpoint.Route);
                var requiresBody = endpoint.HttpMethod is "POST" or "PUT" or "PATCH";

                return new GeneratedTestCase(
                    $"test:{endpoint.Id}",
                    $"{endpoint.HttpMethod} {relativePath}",
                    endpoint,
                    suites,
                    relativePath,
                    new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json"
                    },
                    requiresBody ? "{}" : null,
                    [200, 201, 202, 204],
                    smokeCandidate,
                    smokeCandidate
                        ? "Smoke coverage for a critical or safe endpoint; expects a successful 2xx response as an availability signal."
                        : "Regression coverage for endpoint contract verification with a 2xx-family expectation.");
            })
            .ToArray();
    }

    private static bool IsSmokeCandidate(ApiEndpoint endpoint)
    {
        var route = endpoint.Route.ToLowerInvariant();
        return route.Contains("health", StringComparison.Ordinal)
            || route.Contains("status", StringComparison.Ordinal)
            || route.Contains("ping", StringComparison.Ordinal)
            || (endpoint.HttpMethod is "GET" or "HEAD" && !route.Contains('{'));
    }

    private static string ResolveRoute(string route)
    {
        var resolved = RouteTokenRegex.Replace(route, static match =>
        {
            var token = match.Groups[1].Value.ToLowerInvariant();
            return token switch
            {
                var value when value.Contains("id", StringComparison.Ordinal) => "1",
                var value when value.Contains("guid", StringComparison.Ordinal) => Guid.Empty.ToString(),
                _ => "sample"
            };
        });

        return resolved.StartsWith('/') ? resolved : "/" + resolved;
    }

    [GeneratedRegex("\\{([^}:]+)(?::[^}]+)?\\}")]
    private static partial Regex RouteTokenPattern();
}
