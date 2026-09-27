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
        var startedAt = DateTimeOffset.UtcNow;
        var analysis = await _repositoryAnalyzer.AnalyzeAsync(repositoryPath, cancellationToken);
        var testCases = _testCaseGenerator.Generate(analysis);
        var selection = _testRunPlanner.Plan(testCases, triggerEvent);
        var results = await _testExecutor.ExecuteAsync(apiBaseUrl, selection, cancellationToken);
        var summary = _testResultAnalyzer.Analyze(results);
        var report = new TestRunReport(
            Guid.NewGuid().ToString("n"),
            repositoryPath,
            apiBaseUrl,
            triggerEvent,
            startedAt,
            DateTimeOffset.UtcNow,
            analysis,
            testCases,
            results,
            summary);

        await _reportStore.SaveAsync(report, cancellationToken);
        return report;
    }
}
