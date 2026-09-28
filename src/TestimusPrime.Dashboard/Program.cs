using TestimusPrime.Core.Storage;

var builder = WebApplication.CreateBuilder(args);
var reportDirectory = builder.Configuration["ReportDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "artifacts", "testruns");

builder.Services.AddSingleton(new JsonReportStore(reportDirectory));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/summary", async (JsonReportStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.BuildDashboardSummaryAsync(cancellationToken)));

app.MapGet("/api/runs", async (JsonReportStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.LoadAllAsync(cancellationToken)));

app.MapGet("/api/runs/{runId}", async (string runId, JsonReportStore store, CancellationToken cancellationToken) =>
{
    var report = await store.LoadAsync(runId, cancellationToken);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.Run();
