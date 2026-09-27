using System.Text.RegularExpressions;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed partial class TestCaseGenerator
{
    private static readonly Regex RouteTokenRegex = RouteTokenPattern();
    private static readonly IReadOnlyList<int> SuccessStatusCodes = [200, 201, 202, 204];
    private static readonly IReadOnlyList<int> InvalidRouteStatusCodes = [400, 404];
    private static readonly IReadOnlyList<int> InvalidPayloadStatusCodes = [400];

    public IReadOnlyList<GeneratedTestCase> Generate(RepositoryAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return analysis.Endpoints
            .SelectMany(CreateTestCases)
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

    private static IEnumerable<GeneratedTestCase> CreateTestCases(ApiEndpoint endpoint)
    {
        var smokeCandidate = IsSmokeCandidate(endpoint);
        var positiveSuites = smokeCandidate
            ? new[] { TestSuite.Smoke, TestSuite.Regression }
            : new[] { TestSuite.Regression };
        var relativePath = ResolveRoute(endpoint.Route);
        var requiresBody = endpoint.HttpMethod is "POST" or "PUT" or "PATCH";

        yield return new GeneratedTestCase(
            $"test:{endpoint.Id}",
            $"{endpoint.HttpMethod} {relativePath}",
            endpoint,
            positiveSuites,
            relativePath,
            CreateHeaders(),
            requiresBody ? "{}" : null,
            SuccessStatusCodes,
            smokeCandidate,
            smokeCandidate
                ? "Smoke coverage for a critical or safe endpoint; expects a successful 2xx response as an availability signal."
                : "Regression coverage for endpoint contract verification with a 2xx-family expectation.");

        if (HasRouteTokens(endpoint.Route))
        {
            var invalidRoute = ResolveInvalidRoute(endpoint.Route);
            yield return new GeneratedTestCase(
                $"test:{endpoint.Id}:invalid-route",
                $"{endpoint.HttpMethod} {invalidRoute}",
                endpoint,
                [TestSuite.Regression],
                invalidRoute,
                CreateHeaders(),
                requiresBody ? "{}" : null,
                InvalidRouteStatusCodes,
                false,
                "Negative regression coverage that injects invalid route values and expects the API to reject them with a client error.");
        }

        if (requiresBody)
        {
            yield return new GeneratedTestCase(
                $"test:{endpoint.Id}:invalid-body",
                $"{endpoint.HttpMethod} {relativePath} rejects malformed JSON",
                endpoint,
                [TestSuite.Regression],
                relativePath,
                CreateHeaders(),
                "{",
                InvalidPayloadStatusCodes,
                false,
                "Negative regression coverage that sends malformed JSON to verify the API returns the expected client error.");
        }
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

    private static string ResolveInvalidRoute(string route)
    {
        var resolved = RouteTokenRegex.Replace(route, static match =>
        {
            var token = match.Groups[1].Value.ToLowerInvariant();
            var constraint = match.Groups[2].Value.ToLowerInvariant();
            return token.Contains("guid", StringComparison.Ordinal)
                   || constraint.Contains("guid", StringComparison.Ordinal)
                ? "not-a-guid"
                : "invalid";
        });

        return resolved.StartsWith('/') ? resolved : "/" + resolved;
    }

    private static bool HasRouteTokens(string route) => RouteTokenRegex.IsMatch(route);

    private static Dictionary<string, string> CreateHeaders() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json"
        };

    [GeneratedRegex("\\{([^}:]+)(?::([^}]+))?\\}")]
    private static partial Regex RouteTokenPattern();
}
