using TestimusPrime.Core.Models;

namespace TestimusPrime.Tests;

public sealed class RunnerOptionsTests
{
    [Fact]
    public void Parse_UsesDefaultsForOptionalArguments()
    {
        var options = RunnerOptions.Parse([
            "--repo=/repo",
            "--api-base-url=https://example.test"
        ]);

        options.Mode.Should().Be(RunnerMode.Run);
        options.RepositoryPath.Should().Be(Path.GetFullPath("/repo"));
        options.OutputDirectory.Should().Be(Path.GetFullPath("/repo/artifacts/testruns"));
        options.TriggerEvent.Should().Be(TriggerEvent.Commit);
        options.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Parse_GenerateMode_UsesGeneratedSuiteDefaults()
    {
        var options = RunnerOptions.Parse([
            "--mode=Generate",
            "--repo=/repo"
        ]);

        options.Mode.Should().Be(RunnerMode.Generate);
        options.RepositoryPath.Should().Be(Path.GetFullPath("/repo"));
        options.OutputDirectory.Should().Be(Path.GetFullPath("/repo/artifacts/generated-testcases"));
        options.ApiBaseUrl.Should().BeNull();
    }

    [Fact]
    public void Parse_ExecuteGeneratedMode_RequiresTestSuiteAndApiBaseUrl()
    {
        var options = RunnerOptions.Parse([
            "--mode=ExecuteGenerated",
            "--test-suite=/tmp/generated-suite.json",
            "--api-base-url=https://example.test"
        ]);

        options.Mode.Should().Be(RunnerMode.ExecuteGenerated);
        options.TestSuitePath.Should().Be(Path.GetFullPath("/tmp/generated-suite.json"));
        options.OutputDirectory.Should().Be(Path.GetFullPath("/tmp/testruns"));
    }

    [Fact]
    public void Parse_ExecuteGeneratedMode_ResolvesRelativeOutputFromSuiteDirectory()
    {
        var options = RunnerOptions.Parse([
            "--mode=ExecuteGenerated",
            "--test-suite=/tmp/generated-suite.json",
            "--api-base-url=https://example.test",
            "--output=reports"
        ]);

        options.OutputDirectory.Should().Be(Path.GetFullPath("/tmp/reports"));
    }

    [Fact]
    public void Parse_ThrowsForNonPositiveTimeout()
    {
        var parse = () => RunnerOptions.Parse([
            "--repo=/repo",
            "--api-base-url=https://example.test",
            "--timeout-seconds=0"
        ]);

        parse.Should().Throw<ArgumentException>().WithMessage("*timeout-seconds*");
    }

    [Fact]
    public void Parse_ThrowsForInvalidApiBaseUrl()
    {
        var parse = () => RunnerOptions.Parse([
            "--repo=/repo",
            "--api-base-url=relative-url"
        ]);

        parse.Should().Throw<ArgumentException>().WithMessage("*api-base-url*");
    }

    [Fact]
    public void Parse_ThrowsWhenGeneratedExecutionOmitsTestSuite()
    {
        var parse = () => RunnerOptions.Parse([
            "--mode=ExecuteGenerated",
            "--api-base-url=https://example.test"
        ]);

        parse.Should().Throw<ArgumentException>().WithMessage("*test-suite*");
    }

    [Fact]
    public void Parse_ConfigResolvesPathsAndAppliesOverrides()
    {
        WithConfig("""
            {
              "repo": "api",
              "api-base-url": "https://example.test",
              "output": "reports",
              "trigger": "PullRequest",
              "timeout-seconds": 45,
              "history-limit": 5,
              "dashboard-subdirectory": "quality/api"
            }
            """, (config, directory) =>
        {
            var options = RunnerOptions.Parse([$"--config={config}", "--timeout-seconds=12", "--output=overridden"]);

            options.RepositoryPath.Should().Be(Path.Combine(directory, "api"));
            options.OutputDirectory.Should().Be(Path.Combine(directory, "overridden"));
            options.TriggerEvent.Should().Be(TriggerEvent.PullRequest);
            options.TimeoutSeconds.Should().Be(12);
            options.HistoryLimit.Should().Be(5);
            options.DashboardSubdirectory.Should().Be("quality/api");
        });
    }

    [Fact]
    public void Parse_ConfigGeneratedSuiteAndDefaultsAreConfigRelative()
    {
        WithConfig("""
            {"mode":"ExecuteGenerated","test-suite":"suites/v1.json","api-base-url":"https://example.test"}
            """, (config, directory) =>
        {
            var options = RunnerOptions.Parse([$"--config={config}"]);
            options.TestSuitePath.Should().Be(Path.Combine(directory, "suites", "v1.json"));
            options.OutputDirectory.Should().Be(Path.Combine(directory, "artifacts", "testruns"));
        });
    }

    [Fact]
    public void Parse_PublishDashboardDoesNotRequireApi()
    {
        WithConfig("""
            {"output":"reports","history-limit":7,"dashboard-subdirectory":"tests"}
            """, (config, directory) =>
        {
            var options = RunnerOptions.Parse([$"--config={config}", "--mode=PublishDashboard", "--output-site=site"]);
            options.ReportsDirectory.Should().Be(Path.Combine(directory, "reports"));
            options.OutputSite.Should().Be(Path.Combine(directory, "site"));
            options.HistoryLimit.Should().Be(7);
        });
    }

    [Theory]
    [InlineData("""{"mode":"Invalid"}""")]
    [InlineData("""{"trigger":"Invalid"}""")]
    [InlineData("""{"history-limit":0}""")]
    [InlineData("""{"timeout-seconds":-1}""")]
    [InlineData("""{"api-base-url":"file:///tmp/data"}""")]
    [InlineData("""{"dashboard-subdirectory":"../other"}""")]
    [InlineData("""{"dashboard-subdirectory":"/absolute"}""")]
    [InlineData("""{"dashboard-subdirectory":"a\\b"}""")]
    [InlineData("""{"dashboard-subdirectory":"a//b"}""")]
    [InlineData("""{"dashboard-subdirectory":".git"}""")]
    [InlineData("""{"unknown":"value"}""")]
    [InlineData("""{"mode":"Run","MODE":"Generate"}""")]
    [InlineData("""{"repo":true}""")]
    [InlineData("""[]""")]
    public void Parse_RejectsInvalidConfiguration(string json)
    {
        WithConfig(json, (config, _) =>
        {
            var parse = () => RunnerOptions.Parse([$"--config={config}", "--repo=/repo", "--api-base-url=https://example.test"]);
            if (json.Contains("file:///"))
            {
                parse = () => RunnerOptions.Parse([$"--config={config}", "--repo=/repo"]);
            }
            parse.Should().Throw<ArgumentException>();
        });
    }

    [Fact]
    public void Parse_ExplicitMissingConfigFails()
    {
        var parse = () => RunnerOptions.Parse([$"--config={Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")}"]);
        parse.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("--mode=Typo")]
    [InlineData("--trigger=Typo")]
    [InlineData("--history-limit=0")]
    [InlineData("--typo=value")]
    [InlineData("--repo")]
    public void Parse_RejectsInvalidArguments(string argument)
    {
        var parse = () => RunnerOptions.Parse(["--repo=/repo", "--api-base-url=https://example.test", argument]);
        parse.Should().Throw<ArgumentException>();
    }

    private static void WithConfig(string json, Action<string, string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"testimus-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "testimusprime.json");
            File.WriteAllText(config, json);
            test(config, directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
