using TestimusPrime.Core.Models;
using TestimusPrime.Core.Services;
using TestimusPrime.Core.Storage;

var options = RunnerOptions.Parse(args);
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
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
        break;
    }
}

internal enum RunnerMode
{
    Run,
    Generate,
    ExecuteGenerated
}

internal sealed record RunnerOptions(
    RunnerMode Mode,
    string? RepositoryPath,
    string? ApiBaseUrl,
    string OutputDirectory,
    TriggerEvent TriggerEvent,
    int TimeoutSeconds,
    string? TestSuitePath,
    string? VersionTag)
{
    private const int DefaultTimeoutSeconds = 30;

    public static RunnerOptions Parse(string[] args)
    {
        var values = args
            .Select(argument => argument.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].TrimStart('-'), parts => parts[1], StringComparer.OrdinalIgnoreCase);

        var mode = values.TryGetValue("mode", out var rawMode) && Enum.TryParse<RunnerMode>(rawMode, true, out var parsedMode)
            ? parsedMode
            : RunnerMode.Run;

        values.TryGetValue("repo", out var repositoryPath);
        repositoryPath = string.IsNullOrWhiteSpace(repositoryPath) ? null : Path.GetFullPath(repositoryPath);

        values.TryGetValue("api-base-url", out var apiBaseUrl);
        apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl;

        values.TryGetValue("test-suite", out var testSuitePath);
        testSuitePath = string.IsNullOrWhiteSpace(testSuitePath) ? null : Path.GetFullPath(testSuitePath);

        if ((mode is RunnerMode.Run or RunnerMode.Generate) && string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Provide --repo=<path>.");
        }

        if ((mode is RunnerMode.Run or RunnerMode.ExecuteGenerated)
            && (string.IsNullOrWhiteSpace(apiBaseUrl) || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out _)))
        {
            throw new ArgumentException("Provide --api-base-url=<absolute-url>.");
        }

        if (mode == RunnerMode.ExecuteGenerated && string.IsNullOrWhiteSpace(testSuitePath))
        {
            throw new ArgumentException("Provide --test-suite=<path>.");
        }

        var outputDirectory = values.TryGetValue("output", out var output) && !string.IsNullOrWhiteSpace(output)
            ? output
            : ResolveDefaultOutputDirectory(mode, repositoryPath, testSuitePath);

        var triggerEvent = values.TryGetValue("trigger", out var trigger) && Enum.TryParse<TriggerEvent>(trigger, true, out var parsedTrigger)
            ? parsedTrigger
            : TriggerEvent.Commit;

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

        return new RunnerOptions(
            mode,
            repositoryPath,
            apiBaseUrl,
            Path.GetFullPath(outputDirectory),
            triggerEvent,
            timeoutSeconds,
            testSuitePath,
            versionTag);
    }

    private static string ResolveDefaultOutputDirectory(RunnerMode mode, string? repositoryPath, string? testSuitePath) => mode switch
    {
        RunnerMode.Generate => Path.Combine(repositoryPath!, "artifacts", "generated-testcases"),
        RunnerMode.ExecuteGenerated => Path.Combine(Path.GetDirectoryName(testSuitePath!)!, "testruns"),
        _ => Path.Combine(repositoryPath!, "artifacts", "testruns")
    };
}
