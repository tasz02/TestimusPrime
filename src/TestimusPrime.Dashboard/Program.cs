using System.Text;
using TestimusPrime.Core.Storage;

var builder = WebApplication.CreateBuilder(args);
var reportDirectory = builder.Configuration["ReportDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "artifacts", "testruns");

builder.Services.AddSingleton(new JsonReportStore(reportDirectory));

var app = builder.Build();

app.MapGet("/api/summary", async (JsonReportStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.BuildDashboardSummaryAsync(cancellationToken)));

app.MapGet("/api/runs", async (JsonReportStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.LoadAllAsync(cancellationToken)));

app.MapGet("/api/runs/{runId}", async (string runId, JsonReportStore store, CancellationToken cancellationToken) =>
{
    var report = await store.LoadAsync(runId, cancellationToken);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.MapGet("/", () => Results.Content(BuildDashboardHtml(), "text/html", Encoding.UTF8));

app.Run();

static string BuildDashboardHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>TestimusPrime Dashboard</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 2rem; background: #0f172a; color: #e2e8f0; }
        .metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 1rem; margin-bottom: 2rem; }
        .card, details { background: #1e293b; border-radius: 12px; padding: 1rem; border: 1px solid #334155; }
        h1, h2 { margin-top: 0; }
        .run { margin-bottom: 1rem; }
        table { width: 100%; border-collapse: collapse; }
        th, td { text-align: left; padding: 0.5rem; border-bottom: 1px solid #334155; vertical-align: top; }
        .passed { color: #4ade80; }
        .failed { color: #f87171; }
        code { white-space: pre-wrap; word-break: break-word; }
    </style>
</head>
<body>
    <h1>TestimusPrime Dashboard</h1>
    <div id="metrics" class="metrics"></div>
    <h2>Recent runs</h2>
    <div id="runs"></div>
    <script>
        const metrics = document.getElementById('metrics');
        const runs = document.getElementById('runs');

        const renderMetric = (label, value) => `<div class="card"><strong>${label}</strong><div>${value}</div></div>`;
        const statusClass = passed => passed ? 'passed' : 'failed';

        async function load() {
            const [summaryResponse, runsResponse] = await Promise.all([
                fetch('/api/summary'),
                fetch('/api/runs')
            ]);

            const summary = await summaryResponse.json();
            const reports = await runsResponse.json();

            metrics.innerHTML = [
                renderMetric('Runs', summary.totalRuns),
                renderMetric('Tests', summary.totalTests),
                renderMetric('Passed', summary.passedTests),
                renderMetric('Failed', summary.failedTests),
                renderMetric('Average pass rate', `${summary.averagePassRate}%`)
            ].join('');

            runs.innerHTML = reports.map(report => `
                <details class="run">
                    <summary>${report.runId} · ${report.triggerEvent} · <span class="${statusClass(report.summary.failed === 0)}">${report.summary.passRate}%</span></summary>
                    <p>Completed: ${new Date(report.completedAt).toLocaleString()}</p>
                    <p>Analyzed endpoints: ${report.analysis.endpoints.length} · Executed tests: ${report.summary.total}</p>
                    <table>
                        <thead>
                            <tr><th>Test</th><th>Status</th><th>Request</th><th>Response</th></tr>
                        </thead>
                        <tbody>
                            ${report.results.map(result => `
                                <tr>
                                    <td>${result.testName}<br /><small>${result.suites.join(', ')}</small></td>
                                    <td class="${statusClass(result.passed)}">${result.outcome}</td>
                                    <td><code>${result.request.method} ${result.request.url}\n${result.request.body ?? ''}</code></td>
                                    <td><code>Status: ${result.response.statusCode ?? 'error'}\n${result.response.body ?? result.response.error ?? ''}</code></td>
                                </tr>`).join('')}
                        </tbody>
                    </table>
                </details>`).join('');
        }

        load();
    </script>
</body>
</html>
""";
