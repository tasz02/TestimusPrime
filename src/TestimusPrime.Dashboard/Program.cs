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

        const appendMetric = (label, value) => {
            const card = document.createElement('div');
            card.className = 'card';
            const strong = document.createElement('strong');
            strong.textContent = label;
            const content = document.createElement('div');
            content.textContent = value;
            card.append(strong, content);
            metrics.appendChild(card);
        };

        const createCodeBlock = text => {
            const code = document.createElement('code');
            code.textContent = text;
            return code;
        };

        const createCell = content => {
            const cell = document.createElement('td');
            if (typeof content === 'string') {
                cell.textContent = content;
            } else {
                cell.appendChild(content);
            }
            return cell;
        };

        const statusClass = passed => passed ? 'passed' : 'failed';

        async function load() {
            const [summaryResponse, runsResponse] = await Promise.all([
                fetch('/api/summary'),
                fetch('/api/runs')
            ]);

            const summary = await summaryResponse.json();
            const reports = await runsResponse.json();

            metrics.replaceChildren();
            appendMetric('Runs', String(summary.totalRuns));
            appendMetric('Tests', String(summary.totalTests));
            appendMetric('Passed', String(summary.passedTests));
            appendMetric('Failed', String(summary.failedTests));
            appendMetric('Average pass rate', `${summary.averagePassRate}%`);

            runs.replaceChildren();
            reports.forEach(report => {
                const details = document.createElement('details');
                details.className = 'run';

                const summaryElement = document.createElement('summary');
                const summaryStatus = document.createElement('span');
                summaryStatus.className = statusClass(report.summary.failed === 0);
                summaryStatus.textContent = `${report.summary.passRate}%`;
                summaryElement.append(`${report.runId} · ${report.triggerEvent} · `, summaryStatus);

                const completed = document.createElement('p');
                completed.textContent = `Completed: ${new Date(report.completedAt).toLocaleString()}`;

                const stats = document.createElement('p');
                stats.textContent = `Analyzed endpoints: ${report.analysis.endpoints.length} · Executed tests: ${report.summary.total}`;

                const table = document.createElement('table');
                const headerRow = document.createElement('tr');
                ['Test', 'Status', 'Request', 'Response'].forEach(label => {
                    const header = document.createElement('th');
                    header.textContent = label;
                    headerRow.appendChild(header);
                });
                const thead = document.createElement('thead');
                thead.appendChild(headerRow);
                table.appendChild(thead);

                const tbody = document.createElement('tbody');
                report.results.forEach(result => {
                    const row = document.createElement('tr');

                    const testCell = document.createElement('td');
                    testCell.append(document.createTextNode(result.testName));
                    testCell.appendChild(document.createElement('br'));
                    const suites = document.createElement('small');
                    suites.textContent = result.suites.join(', ');
                    testCell.appendChild(suites);

                    const statusCell = document.createElement('td');
                    statusCell.className = statusClass(result.passed);
                    statusCell.textContent = result.outcome;

                    const requestText = `${result.request.method} ${result.request.url}\n${result.request.body ?? ''}`;
                    const responseText = `Status: ${result.response.statusCode ?? 'error'}\n${result.response.body ?? result.response.error ?? ''}`;

                    row.append(testCell, statusCell, createCell(createCodeBlock(requestText)), createCell(createCodeBlock(responseText)));
                    tbody.appendChild(row);
                });

                table.appendChild(tbody);
                details.append(summaryElement, completed, stats, table);
                runs.appendChild(details);
            });
        }

        load();
    </script>
</body>
</html>
""";
