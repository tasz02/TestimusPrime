using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;
using TestimusPrime.Core.Storage;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("""
        TestimusPrime: generate and execute ASP.NET API tests.
        Usage: testimusprime [--config=testimusprime.json] [--key=value ...]
        Modes: Run (default), Generate, ExecuteGenerated, PublishDashboard.
        Settings: repo, api-base-url, output, trigger (Commit/PullRequest),
          timeout-seconds, test-suite, version-tag, history-limit, dashboard-subdirectory.
        PublishDashboard: reports-directory, existing-site (optional), output-site (fresh directory).
        Relative paths use the config directory when a config is loaded.
        Without --config, testimusprime.json in the current directory is loaded if present.
        Reports include request/response data: use sanitized test data before publishing.
        """);
    return;
}

var options = RunnerOptions.Parse(args);
if (options.Mode == RunnerMode.PublishDashboard)
{
    DashboardPublisher.Publish(
        options.ReportsDirectory!, options.ExistingSite, options.OutputSite!,
        Path.Combine(AppContext.BaseDirectory, "dashboard"),
        options.HistoryLimit, options.DashboardSubdirectory);
    Console.WriteLine($"Dashboard site: {options.OutputSite}");
    return;
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
var orchestrator = new TestFrameworkOrchestrator(
    new RepositoryAnalyzer(),
    new TestCaseGenerator(),
    new TestRunPlanner(),
    new TestExecutor(httpClient),
    new TestResultAnalyzer(),
    new JsonReportStore(options.OutputDirectory));

switch (options.Mode)
{
    case RunnerMode.Generate:
    {
        var generatedSuite = await orchestrator.GenerateAsync(options.RepositoryPath!, options.VersionTag);
        var generatedSuiteStore = new JsonGeneratedTestSuiteStore(options.OutputDirectory);
        var suitePath = await generatedSuiteStore.SaveAsync(generatedSuite);

        Console.WriteLine($"Mode: {options.Mode}");
        Console.WriteLine($"Version tag: {generatedSuite.VersionTag}");
        Console.WriteLine($"Repository: {generatedSuite.RepositoryPath}");
        Console.WriteLine($"Endpoints analyzed: {generatedSuite.Analysis.Endpoints.Count}");
        Console.WriteLine($"Generated tests: {generatedSuite.TestCases.Count}");
        Console.WriteLine($"Artifacts: {suitePath}");
        break;
    }
    case RunnerMode.ExecuteGenerated:
    {
        var generatedSuite = await JsonGeneratedTestSuiteStore.LoadAsync(options.TestSuitePath!);
        var report = await orchestrator.RunGeneratedSuiteAsync(generatedSuite, new Uri(options.ApiBaseUrl!), options.TriggerEvent);

        Console.WriteLine($"Mode: {options.Mode}");
        Console.WriteLine($"Version tag: {generatedSuite.VersionTag}");
        Console.WriteLine($"RunId: {report.RunId}");
        Console.WriteLine($"Trigger: {report.TriggerEvent}");
        Console.WriteLine($"Endpoints analyzed: {report.Analysis.Endpoints.Count}");
        Console.WriteLine($"Generated tests: {report.GeneratedTestCases.Count}");
        Console.WriteLine($"Executed tests: {report.Results.Count}");
        Console.WriteLine($"Pass rate: {report.Summary.PassRate}%");
        Console.WriteLine($"Artifacts: {options.OutputDirectory}");
        Environment.ExitCode = report.Summary.Failed > 0 ? 1 : 0;
        break;
    }
    default:
    {
        var report = await orchestrator.RunAsync(options.RepositoryPath!, new Uri(options.ApiBaseUrl!), options.TriggerEvent);

        Console.WriteLine($"Mode: {options.Mode}");
        Console.WriteLine($"RunId: {report.RunId}");
        Console.WriteLine($"Trigger: {report.TriggerEvent}");
        Console.WriteLine($"Endpoints analyzed: {report.Analysis.Endpoints.Count}");
        Console.WriteLine($"Generated tests: {report.GeneratedTestCases.Count}");
        Console.WriteLine($"Executed tests: {report.Results.Count}");
        Console.WriteLine($"Pass rate: {report.Summary.PassRate}%");
        Console.WriteLine($"Artifacts: {options.OutputDirectory}");
        Environment.ExitCode = report.Summary.Failed > 0 ? 1 : 0;
        break;
    }
}

internal enum RunnerMode
{
    Run,
    Generate,
    ExecuteGenerated,
    PublishDashboard
}

internal sealed record RunnerOptions(
    RunnerMode Mode,
    string? RepositoryPath,
    string? ApiBaseUrl,
    string OutputDirectory,
    TriggerEvent TriggerEvent,
    int TimeoutSeconds,
    string? TestSuitePath,
    string? VersionTag,
    int HistoryLimit,
    string DashboardSubdirectory,
    string? ReportsDirectory,
    string? ExistingSite,
    string? OutputSite)
{
    private const int DefaultTimeoutSeconds = 30;

    public static RunnerOptions Parse(string[] args)
    {
        var overrides = args
            .Select(argument => argument.Split('=', 2, StringSplitOptions.TrimEntries))
            .Select(parts => parts.Length == 2 && parts[0].StartsWith("--", StringComparison.Ordinal)
                ? parts : throw new ArgumentException("Arguments must use --key=value syntax."))
            .ToDictionary(parts => parts[0].TrimStart('-'), parts => parts[1], StringComparer.OrdinalIgnoreCase);

        foreach (var key in overrides.Keys)
        {
            if (!key.Equals("config", StringComparison.OrdinalIgnoreCase) && !RunnerConfiguration.Keys.Contains(key))
            {
                throw new ArgumentException($"Unknown argument: {key}.");
            }
        }
        var (values, configDirectory) = RunnerConfiguration.Load(overrides);

        var mode = ParseEnum(values, "mode", RunnerMode.Run);

        string? ResolvePath(string key)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? Path.GetFullPath(value, configDirectory ?? Environment.CurrentDirectory) : null;
        }

        var repositoryPath = ResolvePath("repo");

        values.TryGetValue("api-base-url", out var apiBaseUrl);
        apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl;

        var testSuitePath = ResolvePath("test-suite");

        if ((mode is RunnerMode.Run or RunnerMode.Generate) && string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Provide --repo=<path>.");
        }

        if ((mode is RunnerMode.Run or RunnerMode.ExecuteGenerated)
            && (string.IsNullOrWhiteSpace(apiBaseUrl)
                || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Provide --api-base-url=<absolute-url>.");
        }

        if (mode == RunnerMode.ExecuteGenerated && string.IsNullOrWhiteSpace(testSuitePath))
        {
            throw new ArgumentException("Provide --test-suite=<path>.");
        }

        var outputDirectory = configDirectory is not null
            ? ResolvePath("output") ?? Path.Combine(configDirectory, "artifacts",
                mode == RunnerMode.Generate ? "generated-testcases" : "testruns")
            : mode == RunnerMode.PublishDashboard
                ? ResolvePath("output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "testruns")
                : ResolveOutputDirectory(mode,
                    values.TryGetValue("output", out var output) ? output : null, repositoryPath, testSuitePath);

        var triggerEvent = ParseEnum(values, "trigger", TriggerEvent.Commit);

        var timeoutSeconds = DefaultTimeoutSeconds;
        if (values.TryGetValue("timeout-seconds", out var timeoutValue))
        {
            if (!int.TryParse(timeoutValue, out timeoutSeconds) || timeoutSeconds <= 0)
            {
                throw new ArgumentException("Provide --timeout-seconds with a positive integer value.");
            }
        }

        var versionTag = values.TryGetValue("version-tag", out var rawVersionTag) && !string.IsNullOrWhiteSpace(rawVersionTag)
            ? rawVersionTag
            : null;

        var historyLimit = 20;
        if (values.TryGetValue("history-limit", out var historyValue)
            && (!int.TryParse(historyValue, out historyLimit) || historyLimit <= 0))
        {
            throw new ArgumentException("Provide --history-limit with a positive integer value.");
        }

        var dashboardSubdirectory = values.GetValueOrDefault("dashboard-subdirectory", "");
        if (Path.IsPathRooted(dashboardSubdirectory)
            || dashboardSubdirectory.Contains('\\')
            || (dashboardSubdirectory.Length > 0
                && dashboardSubdirectory.Split('/').Any(segment =>
                    segment is "" or "." or ".." || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            || dashboardSubdirectory.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '/' and not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Provide --dashboard-subdirectory as a safe relative URL path.");
        }

        var reportsDirectory = ResolvePath("reports-directory") ?? outputDirectory;
        var existingSite = ResolvePath("existing-site");
        var outputSite = ResolvePath("output-site");
        if (mode == RunnerMode.PublishDashboard && outputSite is null)
        {
            throw new ArgumentException("Provide --output-site=<fresh-directory>.");
        }

        return new RunnerOptions(
            mode,
            repositoryPath,
            apiBaseUrl,
            outputDirectory,
            triggerEvent,
            timeoutSeconds,
            testSuitePath,
            versionTag,
            historyLimit,
            dashboardSubdirectory,
            reportsDirectory,
            existingSite,
            outputSite);
    }

    private static T ParseEnum<T>(Dictionary<string, string> values, string key, T fallback) where T : struct, Enum
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }
        if (!Enum.TryParse<T>(value, true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"Provide --{key} with one of: {string.Join(", ", Enum.GetNames<T>())}.");
        }
        return parsed;
    }

    private static string ResolveOutputDirectory(RunnerMode mode, string? output, string? repositoryPath, string? testSuitePath)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return Path.IsPathRooted(output)
                ? Path.GetFullPath(output)
                : Path.GetFullPath(Path.Combine(ResolveModeBaseDirectory(mode, repositoryPath, testSuitePath), output));
        }

        return mode switch
        {
            RunnerMode.Generate => Path.Combine(repositoryPath!, "artifacts", "generated-testcases"),
            RunnerMode.ExecuteGenerated => Path.Combine(Path.GetDirectoryName(testSuitePath!)!, "testruns"),
            _ => Path.Combine(repositoryPath!, "artifacts", "testruns")
        };
    }

    private static string ResolveModeBaseDirectory(RunnerMode mode, string? repositoryPath, string? testSuitePath) => mode switch
    {
        RunnerMode.ExecuteGenerated => Path.GetDirectoryName(testSuitePath!)!,
        _ => repositoryPath!
    };
}
