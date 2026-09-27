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

        options.RepositoryPath.Should().Be(Path.GetFullPath("/repo"));
        options.OutputDirectory.Should().Be(Path.GetFullPath("/repo/artifacts/testruns"));
        options.TriggerEvent.Should().Be(TriggerEvent.Commit);
        options.TimeoutSeconds.Should().Be(30);
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
}
