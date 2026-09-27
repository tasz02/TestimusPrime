using System.Text.RegularExpressions;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed partial class RepositoryAnalyzer
{
    private static readonly string[] HttpAttributeNames = ["HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions"];
    private static readonly Regex ControllerRouteRegex = ControllerRoutePattern();
    private static readonly Regex ControllerActionRegex = ControllerActionPattern();
    private static readonly Regex ControllerClassRegex = ControllerClassPattern();
    private static readonly Regex MinimalApiRegex = MinimalApiPattern();

    public async Task<RepositoryAnalysis> AnalyzeAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        var endpoints = new List<ApiEndpoint>();
        var notes = new List<string>();
        var files = Directory.EnumerateFiles(repositoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);

            endpoints.AddRange(FindControllerEndpoints(file, content));
            endpoints.AddRange(FindMinimalApiEndpoints(file, content));
        }

        if (endpoints.Count == 0)
        {
            notes.Add("No API endpoints were detected. The draft analyzer currently supports ASP.NET controller attributes and minimal API route mappings.");
        }
        else
        {
            notes.Add($"Detected {endpoints.Count} endpoints across {files.Length} C# files.");
        }

        return new RepositoryAnalysis(repositoryPath, DateTimeOffset.UtcNow, endpoints, notes);
    }

    private static IEnumerable<ApiEndpoint> FindControllerEndpoints(string file, string content)
    {
        var classMatches = ControllerClassRegex.Matches(content);
        foreach (Match classMatch in classMatches)
        {
            var controllerName = classMatch.Groups[1].Value;
            var classStart = classMatch.Index;
            var nextClassStart = classMatch.NextMatch().Success ? classMatch.NextMatch().Index : content.Length;
            var classSegment = content[classStart..nextClassStart];
            var routeSearchStart = Math.Max(0, classStart - 400);
            var routeMatch = ControllerRouteRegex.Matches(content[routeSearchStart..classStart]).Cast<Match>().LastOrDefault();
            var controllerRoute = routeMatch is not null && routeMatch.Success ? routeMatch.Groups[1].Value : $"api/{controllerName.Replace("Controller", string.Empty, StringComparison.Ordinal)}";
            controllerRoute = controllerRoute.Replace("[controller]", controllerName.Replace("Controller", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

            foreach (Match actionMatch in ControllerActionRegex.Matches(classSegment))
            {
                var attributeName = actionMatch.Groups[1].Value;
                if (!HttpAttributeNames.Contains(attributeName, StringComparer.Ordinal))
                {
                    continue;
                }

                var actionRoute = actionMatch.Groups[2].Success ? actionMatch.Groups[2].Value : string.Empty;
                var actionName = actionMatch.Groups[3].Value;
                var fullRoute = CombineRoute(controllerRoute, actionRoute);
                var line = GetLineNumber(content, classStart + actionMatch.Index);

                yield return new ApiEndpoint(
                    $"controller:{NormalizeId(file)}:{line}:{attributeName}:{actionName}",
                    attributeName[4..].ToUpperInvariant(),
                    fullRoute,
                    EndpointSourceType.Controller,
                    file,
                    line,
                    controllerName,
                    actionName);
            }
        }
    }

    private static IEnumerable<ApiEndpoint> FindMinimalApiEndpoints(string file, string content)
    {
        foreach (Match match in MinimalApiRegex.Matches(content))
        {
            var methodName = match.Groups[1].Value;
            var route = match.Groups[2].Value;
            var line = GetLineNumber(content, match.Index);

            yield return new ApiEndpoint(
                $"minimal:{NormalizeId(file)}:{line}:{methodName}",
                methodName.ToUpperInvariant(),
                route,
                EndpointSourceType.MinimalApi,
                file,
                line,
                Path.GetFileNameWithoutExtension(file),
                methodName);
        }
    }

    private static string CombineRoute(string prefix, string suffix)
    {
        var normalizedPrefix = prefix.Trim('/');
        var normalizedSuffix = suffix.Trim('/');

        return string.IsNullOrWhiteSpace(normalizedSuffix)
            ? "/" + normalizedPrefix
            : "/" + string.Join('/', new[] { normalizedPrefix, normalizedSuffix }.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string NormalizeId(string value) => value.Replace(Path.DirectorySeparatorChar, '_').Replace(':', '_');

    private static int GetLineNumber(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    [GeneratedRegex(@"\[Route\(""([^""\)]+)""\)\]", RegexOptions.Multiline)]
    private static partial Regex ControllerRoutePattern();

    [GeneratedRegex(@"\[(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch|HttpHead|HttpOptions)(?:\(""([^""]*)""\))?\][\s\S]*?\b(?:async\s+)?(?:Task<[^>]+>|Task|IActionResult|ActionResult(?:<[^>]+>)?|Results<[^>]+>|IResult|[A-Za-z0-9_<>\[\]\?]+)\s+([A-Za-z0-9_]+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex ControllerActionPattern();

    [GeneratedRegex(@"class\s+([A-Za-z0-9_]+Controller)\b", RegexOptions.Multiline)]
    private static partial Regex ControllerClassPattern();

    [GeneratedRegex(@"\.Map(Get|Post|Put|Delete|Patch|Head|Options)\s*\(\s*""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex MinimalApiPattern();
}
