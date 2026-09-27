using System.Text.Json;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Storage;

public sealed class JsonReportStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _outputDirectory;

    public JsonReportStore(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public async Task SaveAsync(TestRunReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        Directory.CreateDirectory(_outputDirectory);
        var reportPath = GetReportPath(report.RunId);
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<TestRunReport>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_outputDirectory))
        {
            return Array.Empty<TestRunReport>();
        }

        var reports = new List<TestRunReport>();
        foreach (var file in Directory.EnumerateFiles(_outputDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            await using var stream = File.OpenRead(file);
            var report = await JsonSerializer.DeserializeAsync<TestRunReport>(stream, SerializerOptions, cancellationToken);
            if (report is not null)
            {
                reports.Add(report);
            }
        }

        return reports
            .OrderByDescending(static report => report.CompletedAt)
            .ToArray();
    }

    public async Task<TestRunReport?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var reportPath = GetReportPath(runId);
        if (!File.Exists(reportPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(reportPath);
        return await JsonSerializer.DeserializeAsync<TestRunReport>(stream, SerializerOptions, cancellationToken);
    }

    public async Task<DashboardSummary> BuildDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var reports = await LoadAllAsync(cancellationToken);
        var totalRuns = reports.Count;
        var totalTests = reports.Sum(static report => report.Summary.Total);
        var passed = reports.Sum(static report => report.Summary.Passed);
        var failed = reports.Sum(static report => report.Summary.Failed);
        var averagePassRate = totalRuns == 0 ? 0 : Math.Round(reports.Average(static report => report.Summary.PassRate), 2);
        var recentRuns = reports
            .Take(10)
            .Select(static report => new RunSummaryCard(report.RunId, report.TriggerEvent, report.CompletedAt, report.Summary.Total, report.Summary.Passed, report.Summary.Failed, report.Summary.PassRate))
            .ToArray();

        return new DashboardSummary(totalRuns, totalTests, passed, failed, averagePassRate, recentRuns);
    }

    private string GetReportPath(string runId)
    {
        var sanitizedRunId = SanitizeRunId(runId);
        return Path.Combine(_outputDirectory, $"{sanitizedRunId}.json");
    }

    private static string SanitizeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run ID is required.", nameof(runId));
        }

        if (runId.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || runId.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Run ID contains invalid path characters.", nameof(runId));
        }

        return runId;
    }
}
