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
}
