using System.Text.Json;
using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Storage;

public sealed class JsonGeneratedTestSuiteStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _outputDirectory;

    public JsonGeneratedTestSuiteStore(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public async Task<string> SaveAsync(GeneratedTestSuite suite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);

        Directory.CreateDirectory(_outputDirectory);
        var suitePath = GetSuitePath(suite.VersionTag);
        await using var stream = File.Create(suitePath);
        await JsonSerializer.SerializeAsync(stream, suite, SerializerOptions, cancellationToken);
        return suitePath;
    }

    public string GetSuitePath(string versionTag)
    {
        var sanitizedVersionTag = SanitizeVersionTag(versionTag);
        return Path.Combine(_outputDirectory, $"{sanitizedVersionTag}.json");
    }

    public static async Task<GeneratedTestSuite> LoadAsync(string suitePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suitePath))
        {
            throw new ArgumentException("Test suite path is required.", nameof(suitePath));
        }

        var fullPath = Path.GetFullPath(suitePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Generated test suite file was not found.", fullPath);
        }

        await using var stream = File.OpenRead(fullPath);
        var suite = await JsonSerializer.DeserializeAsync<GeneratedTestSuite>(stream, SerializerOptions, cancellationToken);
        return suite ?? throw new InvalidOperationException("Generated test suite file was empty or invalid.");
    }

    public static string CreateDefaultVersionTag() => $"testcases-v{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

    private static string SanitizeVersionTag(string versionTag)
    {
        if (string.IsNullOrWhiteSpace(versionTag))
        {
            throw new ArgumentException("Version tag is required.", nameof(versionTag));
        }

        if (versionTag.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || versionTag.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || versionTag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Version tag contains invalid path characters.", nameof(versionTag));
        }

        return versionTag;
    }
}
