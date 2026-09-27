using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed class TestExecutor
{
    private readonly HttpClient _httpClient;

    public TestExecutor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<TestExecutionResult>> ExecuteAsync(Uri apiBaseUrl, ExecutionSelection selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        ArgumentNullException.ThrowIfNull(selection);

        var results = new List<TestExecutionResult>(selection.TestCases.Count);
        foreach (var testCase in selection.TestCases)
        {
            using var request = new HttpRequestMessage(new HttpMethod(testCase.Endpoint.HttpMethod), new Uri(apiBaseUrl, testCase.RelativePath));
            foreach (var header in testCase.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (testCase.Body is not null)
            {
                request.Content = new StringContent(testCase.Body, Encoding.UTF8, "application/json");
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            var sentAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();

                var passed = EvaluateResult(testCase, (int)response.StatusCode);
                results.Add(new TestExecutionResult(
                    testCase.Id,
                    testCase.Name,
                    testCase.Suites,
                    passed,
                    (int)response.StatusCode,
                    passed ? "Passed" : "Failed",
                    passed ? null : $"Expected {DescribeExpected(testCase)} but received {(int)response.StatusCode}.",
                    new RequestLog(testCase.Endpoint.HttpMethod, request.RequestUri!.ToString(), testCase.Headers, testCase.Body, sentAt),
                    new ResponseLog(
                        (int)response.StatusCode,
                        MergeHeaders(response.Headers, response.Content.Headers),
                        responseBody,
                        DateTimeOffset.UtcNow,
                        stopwatch.ElapsedMilliseconds,
                        null)));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                results.Add(new TestExecutionResult(
                    testCase.Id,
                    testCase.Name,
                    testCase.Suites,
                    false,
                    null,
                    "Error",
                    ex.Message,
                    new RequestLog(testCase.Endpoint.HttpMethod, request.RequestUri!.ToString(), testCase.Headers, testCase.Body, sentAt),
                    new ResponseLog(null, new Dictionary<string, string>(), null, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds, ex.ToString())));
            }
        }

        return results;
    }

    private static Dictionary<string, string> MergeHeaders(HttpResponseHeaders responseHeaders, HttpContentHeaders contentHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in responseHeaders.Concat(contentHeaders))
        {
            merged[header.Key] = merged.TryGetValue(header.Key, out var existingValue)
                ? string.Join(",", new[] { existingValue, string.Join(",", header.Value) }.Where(static value => !string.IsNullOrWhiteSpace(value)))
                : string.Join(",", header.Value);
        }

        return merged;
    }

    private static bool EvaluateResult(GeneratedTestCase testCase, int statusCode)
    {
        if (testCase.AllowAnySuccessfulStatus)
        {
            return statusCode is >= 200 and < 300;
        }

        return testCase.ExpectedStatusCodes.Contains(statusCode);
    }

    private static string DescribeExpected(GeneratedTestCase testCase) => testCase.AllowAnySuccessfulStatus
        ? "a 2xx response"
        : $"one of [{string.Join(", ", testCase.ExpectedStatusCodes)}]";
}
