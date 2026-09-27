using TestimusPrime.Core.Models;
using TestimusPrime.Core.Storage;

namespace TestimusPrime.Core.Services;

public sealed class TestFrameworkOrchestrator
{
    private readonly RepositoryAnalyzer _repositoryAnalyzer;
    private readonly TestCaseGenerator _testCaseGenerator;
    private readonly TestRunPlanner _testRunPlanner;
    private readonly TestExecutor _testExecutor;
    private readonly TestResultAnalyzer _testResultAnalyzer;
    private readonly JsonReportStore _reportStore;

    public TestFrameworkOrchestrator(
        RepositoryAnalyzer repositoryAnalyzer,
        TestCaseGenerator testCaseGenerator,
        TestRunPlanner testRunPlanner,
        TestExecutor testExecutor,
        TestResultAnalyzer testResultAnalyzer,
        JsonReportStore reportStore)
    {
        _repositoryAnalyzer = repositoryAnalyzer;
        _testCaseGenerator = testCaseGenerator;
        _testRunPlanner = testRunPlanner;
        _testExecutor = testExecutor;
        _testResultAnalyzer = testResultAnalyzer;
        _reportStore = reportStore;
    }

    public async Task<TestRunReport> RunAsync(string repositoryPath, Uri apiBaseUrl, TriggerEvent triggerEvent, CancellationToken cancellationToken = default)
    {
        var suite = await GenerateAsync(repositoryPath, null, cancellationToken);
        return await RunGeneratedSuiteAsync(suite, apiBaseUrl, triggerEvent, cancellationToken);
    }

    public async Task<GeneratedTestSuite> GenerateAsync(string repositoryPath, string? versionTag = null, CancellationToken cancellationToken = default)
    {
        var analysis = await _repositoryAnalyzer.AnalyzeAsync(repositoryPath, cancellationToken);
        var testCases = _testCaseGenerator.Generate(analysis);
        return new GeneratedTestSuite(
            string.IsNullOrWhiteSpace(versionTag) ? JsonGeneratedTestSuiteStore.CreateDefaultVersionTag() : versionTag,
            repositoryPath,
            DateTimeOffset.UtcNow,
            analysis,
            testCases);
    }

    public async Task<TestRunReport> RunGeneratedSuiteAsync(GeneratedTestSuite suite, Uri apiBaseUrl, TriggerEvent triggerEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);

        var startedAt = DateTimeOffset.UtcNow;
        var selection = _testRunPlanner.Plan(suite.TestCases, triggerEvent);
        var results = await _testExecutor.ExecuteAsync(apiBaseUrl, selection, cancellationToken);
        var summary = _testResultAnalyzer.Analyze(results);
        var report = new TestRunReport(
            Guid.NewGuid().ToString("n"),
            suite.RepositoryPath,
            apiBaseUrl,
            triggerEvent,
            startedAt,
            DateTimeOffset.UtcNow,
            suite.Analysis,
            suite.TestCases,
            results,
            summary);

        await _reportStore.SaveAsync(report, cancellationToken);
        return report;
    }
}
