using System.Text.Json;
using System.Text.Json.Nodes;

namespace TestimusPrime.Tests;

public sealed class DashboardPublisherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $".dashboard-publisher-tests-{Guid.NewGuid():N}");

    public DashboardPublisherTests()
    {
        Directory.CreateDirectory(Reports);
        Directory.CreateDirectory(Assets);
        File.WriteAllText(Path.Combine(Assets, "index.html"), "new dashboard");
    }

    private string Reports => Path.Combine(_root, "reports");
    private string Assets => Path.Combine(_root, "assets");
    private string Existing => Path.Combine(_root, "existing");
    private string Output => Path.Combine(_root, "output");

    [Fact]
    public void Publish_MergesHistoryByRunIdAndRetainsNewestRuns()
    {
        WriteReport(Path.Combine(Existing, "dashboard", "data", "runs"), "old", "2026-01-01T00:00:00Z", 5, 3, 2, 60);
        WriteReport(Path.Combine(Existing, "dashboard", "data", "runs"), "same", "2026-01-02T00:00:00Z", 5, 1, 4, 20);
        WriteReport(Reports, "same", "2026-01-03T00:00:00Z", 5, 5, 0, 100);
        WriteReport(Path.Combine(Reports, "nested"), "new", "2026-01-04T01:00:00+02:00", 10, 9, 1, 90);
        WriteReport(Reports, "later", "2026-01-03T23:30:00Z", 2, 0, 2, 0);

        Publish(3, "dashboard");

        var runs = Read(Path.Combine(Output, "dashboard", "data", "runs.json")).AsArray();
        runs.Select(run => run!["runId"]!.GetValue<string>()).Should().Equal("later", "new", "same");
        runs[2]!["summary"]!["passed"]!.GetValue<int>().Should().Be(5);
        var summary = Read(Path.Combine(Output, "dashboard", "data", "summary.json"));
        summary["totalRuns"]!.GetValue<int>().Should().Be(3);
        summary["totalTests"]!.GetValue<int>().Should().Be(17);
        summary["passedTests"]!.GetValue<int>().Should().Be(14);
        summary["failedTests"]!.GetValue<int>().Should().Be(3);
        summary["averagePassRate"]!.GetValue<double>().Should().Be(63.33);
        Directory.GetFiles(Path.Combine(Output, "dashboard", "data", "runs"))
            .Select(Path.GetFileName).Should().BeEquivalentTo("later.json", "new.json", "same.json");
        File.Exists(Path.Combine(Existing, "dashboard", "data", "runs", "old.json")).Should().BeTrue();
    }

    [Fact]
    public void Publish_SubdirectoryPreservesOtherSiteContentAndReplacesDashboard()
    {
        WriteFile(Path.Combine(Existing, "index.html"), "landing page");
        WriteFile(Path.Combine(Existing, "CNAME"), "example.org");
        WriteFile(Path.Combine(Existing, "docs", "reference.html"), "reference");
        WriteFile(Path.Combine(Existing, "apps", "other", "index.html"), "other app");
        WriteFile(Path.Combine(Existing, "apps", "dashboard", "stale.txt"), "remove me");
        Directory.CreateDirectory(Path.Combine(Existing, "empty"));
        Directory.CreateDirectory(Output);

        Publish(20, "apps/dashboard");

        File.ReadAllText(Path.Combine(Output, "index.html")).Should().Be("landing page");
        File.ReadAllText(Path.Combine(Output, "CNAME")).Should().Be("example.org");
        File.ReadAllText(Path.Combine(Output, "docs", "reference.html")).Should().Be("reference");
        File.ReadAllText(Path.Combine(Output, "apps", "other", "index.html")).Should().Be("other app");
        Directory.Exists(Path.Combine(Output, "empty")).Should().BeTrue();
        File.ReadAllText(Path.Combine(Output, "apps", "dashboard", "index.html")).Should().Be("new dashboard");
        File.Exists(Path.Combine(Output, "apps", "dashboard", "stale.txt")).Should().BeFalse();
        File.Exists(Path.Combine(Output, ".nojekyll")).Should().BeTrue();
        File.Exists(Path.Combine(Existing, "apps", "dashboard", "stale.txt")).Should().BeTrue();
    }

    [Fact]
    public void Publish_RootDashboardRefreshesHistoryAndPreservesUnrelatedFiles()
    {
        WriteFile(Path.Combine(Existing, "index.html"), "old dashboard");
        WriteFile(Path.Combine(Existing, "docs", "index.html"), "documentation");
        WriteFile(Path.Combine(Existing, "data", "obsolete.json"), "{}");

        Publish(20, "");

        File.ReadAllText(Path.Combine(Output, "index.html")).Should().Be("new dashboard");
        File.ReadAllText(Path.Combine(Output, "docs", "index.html")).Should().Be("documentation");
        File.Exists(Path.Combine(Output, "data", "obsolete.json")).Should().BeFalse();
        Read(Path.Combine(Output, "data", "runs.json")).AsArray().Should().BeEmpty();
        var summary = Read(Path.Combine(Output, "data", "summary.json"));
        summary["totalRuns"]!.GetValue<int>().Should().Be(0);
        summary["averagePassRate"]!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Publish_DefaultsMissingSummaryAndSkipsGeneratedSuites()
    {
        WriteFile(Path.Combine(Reports, "suite.json"), """{"versionTag":"v1","analysis":{},"testCases":[]}""");
        WriteFile(Path.Combine(Reports, "other.json"), "[]");
        WriteFile(Path.Combine(Reports, "run.json"), """{"runId":"minimal"}""");

        DashboardPublisher.Publish(Reports, null, Output, Assets, 20, "");

        var summary = Read(Path.Combine(Output, "data", "summary.json"));
        summary["totalRuns"]!.GetValue<int>().Should().Be(1);
        summary["totalTests"]!.GetValue<int>().Should().Be(0);
        summary["passedTests"]!.GetValue<int>().Should().Be(0);
        summary["failedTests"]!.GetValue<int>().Should().Be(0);
        summary["averagePassRate"]!.GetValue<double>().Should().Be(0);
        var card = summary["recentRuns"]!.AsArray().Single()!;
        card["runId"]!.GetValue<string>().Should().Be("minimal");
        card["triggerEvent"].Should().BeNull();
        card["completedAt"].Should().BeNull();
        card["passRate"]!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Publish_CapsRecentCardsAtTenAndHonorsHistoryLimitOfOne()
    {
        for (var day = 1; day <= 12; day++)
        {
            WriteReport(Reports, $"run-{day}", $"2026-01-{day:00}T00:00:00Z", 1, 1, 0, 100);
        }

        Publish(20, "");
        Read(Path.Combine(Output, "data", "summary.json"))["recentRuns"]!.AsArray().Should().HaveCount(10);
        var secondOutput = Path.Combine(_root, "second-output");
        DashboardPublisher.Publish(Reports, null, secondOutput, Assets, 1, "");
        Read(Path.Combine(secondOutput, "data", "runs.json")).AsArray()
            .Should().ContainSingle(run => run!["runId"]!.GetValue<string>() == "run-12");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("dashboard/../../outside")]
    [InlineData("/absolute")]
    [InlineData(@"C:\absolute")]
    [InlineData(@"..\outside")]
    [InlineData("dashboard/./nested")]
    [InlineData(".")]
    [InlineData("dashboard//nested")]
    [InlineData("dashboard/")]
    [InlineData(".git")]
    [InlineData("dashboard/.GIT/nested")]
    [InlineData("dashboard name")]
    [InlineData("dashboard\tname")]
    [InlineData("dashboard\nname")]
    [InlineData("dashboard%2fnested")]
    [InlineData("dashboard?query")]
    public void Publish_RejectsUnsafeSubdirectoriesWithoutWriting(string subdirectory)
    {
        var action = () => Publish(20, subdirectory);
        action.Should().Throw<ArgumentException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Publish_RejectsNonpositiveHistoryLimitBeforeWriting(int historyLimit)
    {
        var action = () => Publish(historyLimit, "");
        action.Should().Throw<ArgumentOutOfRangeException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Publish_ExcludesGitFilesAndDirectoriesAtEveryDepth()
    {
        WriteFile(Path.Combine(Existing, ".git", "config"), "private checkout metadata");
        WriteFile(Path.Combine(Existing, "docs", ".git"), "gitdir: private worktree metadata");
        WriteFile(Path.Combine(Existing, "docs", "nested", ".GIT", "config"), "private nested metadata");
        WriteFile(Path.Combine(Existing, "docs", "reference.txt"), "preserved documentation");
        WriteFile(Path.Combine(Assets, ".git", "config"), "private assets metadata");
        WriteFile(Path.Combine(Assets, "nested", ".git"), "gitdir: private assets worktree metadata");
        WriteFile(Path.Combine(Reports, ".git", "invalid.json"), "not a report");
        WriteFile(Path.Combine(Reports, "nested", ".git"), "not JSON");
        WriteReport(Reports, "valid", "2026-01-01T00:00:00Z", 1, 1, 0, 100);

        Publish(20, "dashboard");

        Directory.EnumerateFileSystemEntries(Output, "*", SearchOption.AllDirectories)
            .Should().NotContain(path => Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase));
        File.ReadAllText(Path.Combine(Output, "docs", "reference.txt")).Should().Be("preserved documentation");
        Read(Path.Combine(Output, "dashboard", "data", "runs.json")).AsArray().Should().ContainSingle();
        File.ReadAllText(Path.Combine(Existing, ".git", "config")).Should().Be("private checkout metadata");
    }

    [Theory]
    [InlineData("reports", false)]
    [InlineData("assets", false)]
    [InlineData("existing", false)]
    [InlineData("reports", true)]
    [InlineData("assets", true)]
    [InlineData("existing", true)]
    public void Publish_RejectsOutputEqualToOrInsideInputs(string input, bool nested)
    {
        Directory.CreateDirectory(Existing);
        var path = Path.Combine(_root, input);
        var output = nested ? Path.Combine(path, "output") : path;
        var action = () => DashboardPublisher.Publish(Reports, Existing, output, Assets, 20, "dashboard");
        action.Should().Throw<ArgumentException>();
        File.ReadAllText(Path.Combine(Assets, "index.html")).Should().Be("new dashboard");
    }

    [Fact]
    public void Publish_RejectsOutputContainingInputs()
    {
        var action = () => DashboardPublisher.Publish(Reports, null, _root, Assets, 20, "");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Publish_RejectsNonemptyOutputWithoutDeletingItsContent()
    {
        WriteFile(Path.Combine(Output, "keep.txt"), "do not delete");
        var action = () => Publish(20, "");
        action.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(Output, "keep.txt")).Should().Be("do not delete");
    }

    [Theory]
    [InlineData("reports")]
    [InlineData("assets")]
    [InlineData("existing")]
    public void Publish_RejectsLinksInsideInputs(string input)
    {
        Directory.CreateDirectory(Existing);
        File.CreateSymbolicLink(Path.Combine(_root, input, "linked.html"), Path.Combine(Assets, "index.html"));
        var action = () => Publish(20, "");
        action.Should().Throw<IOException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Publish_RejectsLinkedOutputAncestor()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        var action = () => DashboardPublisher.Publish(Reports, null, Path.Combine(link, "output"), Assets, 20, "");
        action.Should().Throw<IOException>();
        Directory.Exists(Path.Combine(target, "output")).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"runId":123}""")]
    [InlineData("""{"runId":""}""")]
    [InlineData("""{"runId":"bad","summary":[]}""")]
    [InlineData("""{"runId":"bad","results":{}}""")]
    [InlineData("""{"runId":"bad","results":[null]}""")]
    [InlineData("""{"runId":"bad","summary":{"suites":{}}}""")]
    [InlineData("""{"runId":"bad","completedAt":123}""")]
    [InlineData("""{"results":[]}""")]
    public void Publish_RejectsMalformedRunReportsBeforeWriting(string report)
    {
        WriteFile(Path.Combine(Reports, "bad.json"), report);
        var action = () => Publish(20, "");
        action.Should().Throw<InvalidDataException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Publish_RejectsInvalidJsonBeforeWriting()
    {
        WriteFile(Path.Combine(Reports, "bad.json"), """{"runId":"bad","summary":""");
        var action = () => Publish(20, "");
        action.Should().Throw<JsonException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Publish_RejectsCollidingSanitizedRunIds()
    {
        WriteFile(Path.Combine(Reports, "first.json"), """{"runId":"a/b"}""");
        WriteFile(Path.Combine(Reports, "second.json"), """{"runId":"a-b"}""");
        var action = () => Publish(20, "");
        action.Should().Throw<InvalidDataException>();
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Publish_KeepsReportTextAsJsonAndSanitizesRunFilenames()
    {
        const string malicious = "<img src=x onerror=alert(1)>";
        WriteFile(Path.Combine(Reports, "run.json"),
            JsonSerializer.Serialize(new { runId = "../../outside", results = new[] { new { testName = malicious } } }));

        Publish(20, "");

        Read(Path.Combine(Output, "data", "runs.json"))[0]!["results"]![0]!["testName"]!
            .GetValue<string>().Should().Be(malicious);
        File.Exists(Path.Combine(Output, "data", "runs", "..-..-outside.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(Output, "data", "runs.json")).Should().NotContain("<img");
        File.Exists(Path.Combine(_root, "outside.json")).Should().BeFalse();
    }

    [Fact]
    public void Publish_LoadsExistingRunsArrayWhenPerRunHistoryIsAbsent()
    {
        WriteFile(Path.Combine(Existing, "dashboard", "data", "runs.json"),
            """[{"runId":"old","completedAt":"2026-01-01T00:00:00Z","summary":{"total":"5","passed":"4","failed":"1","passRate":"80"}}]""");

        Publish(20, "dashboard");

        Read(Path.Combine(Output, "dashboard", "data", "summary.json"))["totalTests"]!.GetValue<int>().Should().Be(5);
        Read(Path.Combine(Output, "dashboard", "data", "runs.json")).AsArray().Should().ContainSingle();
    }

    private void Publish(int limit, string subdirectory) =>
        DashboardPublisher.Publish(Reports, Existing, Output, Assets, limit, subdirectory);

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    private static void WriteReport(string directory, string id, string completedAt, int total, int passed, int failed, double passRate) =>
        WriteFile(Path.Combine(directory, $"{id}.json"), JsonSerializer.Serialize(new
        {
            runId = id,
            triggerEvent = 0,
            completedAt,
            summary = new { total, passed, failed, passRate }
        }));

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
