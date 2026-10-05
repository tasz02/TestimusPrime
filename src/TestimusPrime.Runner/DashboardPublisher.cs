using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class DashboardPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static void Publish(
        string reportsDirectory,
        string? existingSite,
        string outputSite,
        string assetsDirectory,
        int historyLimit,
        string dashboardSubdirectory)
    {
        ArgumentNullException.ThrowIfNull(dashboardSubdirectory);
        var subdirectory = ValidateSubdirectory(dashboardSubdirectory);
        var reports = NormalizePath(reportsDirectory);
        var assets = NormalizePath(assetsDirectory);
        var output = NormalizePath(outputSite);
        var existing = existingSite is null ? null : NormalizePath(existingSite);

        foreach (var input in new[] { reports, assets, existing }.OfType<string>())
        {
            if (ContainsPath(input, output) || ContainsPath(output, input))
            {
                throw new ArgumentException("The output site must not overlap any input directory.");
            }

            RejectLinkedAncestors(input);
        }

        RejectLinkedAncestors(output);
        if (File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
        {
            throw new IOException("The output site must be nonexistent or an empty directory.");
        }

        if (!Directory.Exists(reports) || !Directory.Exists(assets))
        {
            throw new DirectoryNotFoundException("Reports and dashboard assets directories must exist.");
        }

        if (existing is not null && File.Exists(existing))
        {
            throw new IOException("The existing site must be a directory.");
        }

        var reportFiles = GetSafeFiles(reports);
        var assetFiles = GetSafeFiles(assets);
        var existingFiles = existing is not null && Directory.Exists(existing) ? GetSafeFiles(existing) : [];
        var merged = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var existingDashboard = existing is null ? null : Path.Combine(existing, subdirectory);
        if (existingDashboard is not null)
        {
            var history = Path.Combine(existingDashboard, "data", "runs");
            foreach (var file in existingFiles.Where(file =>
                         string.Equals(Path.GetDirectoryName(file), history, PathComparison) && IsJson(file)))
            {
                AddReport(merged, JsonNode.Parse(File.ReadAllText(file)), file);
            }

            var runsFile = Path.Combine(existingDashboard, "data", "runs.json");
            if (!Directory.Exists(history) && File.Exists(runsFile))
            {
                if (JsonNode.Parse(File.ReadAllText(runsFile)) is not JsonArray runs)
                {
                    throw new InvalidDataException($"Invalid dashboard history: {runsFile}");
                }

                foreach (var run in runs)
                {
                    AddReport(merged, run?.DeepClone(), runsFile);
                }
            }
        }

        foreach (var file in reportFiles.Where(IsJson).Order(StringComparer.Ordinal))
        {
            AddReport(merged, JsonNode.Parse(File.ReadAllText(file)), file);
        }

        var kept = merged.Values
            .OrderByDescending(CompletedAt)
            .Take(Math.Max(historyLimit, 1))
            .ToArray();
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in kept)
        {
            if (!filenames.Add(RunFilename(run["runId"]!.GetValue<string>())))
            {
                throw new InvalidDataException("Run IDs map to the same dashboard filename.");
            }
        }

        var summary = BuildSummary(kept);
        Directory.CreateDirectory(output);
        if (existing is not null)
        {
            CopySite(existing, output, subdirectory);
        }

        var dashboard = Path.Combine(output, subdirectory);
        Directory.CreateDirectory(dashboard);
        foreach (var file in assetFiles)
        {
            var destination = Path.Combine(dashboard, Path.GetRelativePath(assets, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        var data = Path.Combine(dashboard, "data");
        Directory.CreateDirectory(Path.Combine(data, "runs"));
        WriteJson(Path.Combine(data, "summary.json"), summary);
        WriteJson(Path.Combine(data, "runs.json"), kept);
        foreach (var run in kept)
        {
            WriteJson(Path.Combine(data, "runs", RunFilename(run["runId"]!.GetValue<string>())), run);
        }

        File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
    }

    private static void AddReport(Dictionary<string, JsonObject> reports, JsonNode? node, string file)
    {
        if (node is not JsonObject report)
        {
            return;
        }

        if (!report.ContainsKey("runId"))
        {
            if (report.ContainsKey("results") || report.ContainsKey("completedAt"))
            {
                throw new InvalidDataException($"Run report has no runId: {file}");
            }

            return;
        }

        if (report["runId"] is not JsonValue idValue
            || !idValue.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id)
            || (report["summary"] is not null && report["summary"] is not JsonObject)
            || !IsObjectArrayOrNull(report["results"])
            || !IsObjectArrayOrNull(report["summary"]?["suites"])
            || (report["completedAt"] is not null
                && (report["completedAt"] is not JsonValue completedAt || !completedAt.TryGetValue<string>(out _))))
        {
            throw new InvalidDataException($"Invalid run report: {file}");
        }

        reports[id] = report;
    }

    private static bool IsObjectArrayOrNull(JsonNode? value) =>
        value is null || (value is JsonArray array && array.All(item => item is JsonObject));

    private static object BuildSummary(JsonObject[] runs) => new
    {
        totalRuns = runs.Length,
        totalTests = runs.Sum(run => Integer(run["summary"]?["total"])),
        passedTests = runs.Sum(run => Integer(run["summary"]?["passed"])),
        failedTests = runs.Sum(run => Integer(run["summary"]?["failed"])),
        averagePassRate = runs.Length == 0 ? 0 : Math.Round(runs.Average(run => Number(run["summary"]?["passRate"])), 2),
        recentRuns = runs.Take(10).Select(run => new
        {
            runId = run["runId"],
            triggerEvent = run["triggerEvent"],
            completedAt = run["completedAt"],
            total = Integer(run["summary"]?["total"]),
            passed = Integer(run["summary"]?["passed"]),
            failed = Integer(run["summary"]?["failed"]),
            passRate = Math.Round(Number(run["summary"]?["passRate"]), 2)
        }).ToArray()
    };

    private static double Number(JsonNode? value) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) ? number : 0;

    private static long Integer(JsonNode? value)
    {
        var number = Number(value);
        return number >= long.MinValue && number < long.MaxValue ? (long)number : 0;
    }

    private static DateTimeOffset CompletedAt(JsonObject run) =>
        run["completedAt"] is JsonValue value && value.TryGetValue<string>(out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;

    private static string RunFilename(string id)
    {
        var sanitized = Regex.Replace(id, "[^A-Za-z0-9._-]", "-").Trim('-');
        if (sanitized.Length == 0)
        {
            throw new InvalidDataException("Run ID must contain at least one valid filename character.");
        }

        return sanitized + ".json";
    }

    private static bool IsJson(string file) => Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase);
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string ValidateSubdirectory(string value)
    {
        if (value.Length == 0)
        {
            return "";
        }

        if (Path.IsPathRooted(value) || value.Contains('\\') || value.Contains(':')
            || value.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Dashboard subdirectory must be a safe relative path.", nameof(value));
        }

        return Path.Combine(value.Split('/'));
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool ContainsPath(string parent, string child) =>
        parent.Equals(child, PathComparison)
        || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, PathComparison);

    private static void RejectLinkedAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
            {
                throw new IOException($"Symbolic links are not allowed in publisher paths: {current}");
            }
        }
    }

    private static string[] GetSafeFiles(string directory)
    {
        var files = new List<string>();
        void Visit(string path)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Symbolic links are not allowed in publisher inputs: {entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Visit(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        Visit(directory);
        return files.ToArray();
    }

    private static void CopySite(string source, string output, string subdirectory)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        var refreshed = Path.Combine(source, subdirectory.Length == 0 ? "data" : subdirectory);
        void Copy(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (ContainsPath(refreshed, entry))
                {
                    continue;
                }

                var destination = Path.Combine(output, Path.GetRelativePath(source, entry));
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    Copy(entry);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(entry, destination);
                }
            }
        }

        Copy(source);
    }
}
