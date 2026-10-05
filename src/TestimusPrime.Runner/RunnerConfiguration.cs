using System.Text.Json;

internal static class RunnerConfiguration
{
    internal static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "repo", "api-base-url", "output", "mode", "trigger", "timeout-seconds",
        "test-suite", "version-tag", "history-limit", "dashboard-subdirectory",
        "reports-directory", "existing-site", "output-site"
    };

    public static (Dictionary<string, string> Values, string? BaseDirectory) Load(
        Dictionary<string, string> overrides)
    {
        var explicitConfig = overrides.TryGetValue("config", out var configPath);
        configPath = explicitConfig ? configPath : Path.Combine(Environment.CurrentDirectory, "testimusprime.json");
        if (!explicitConfig && !File.Exists(configPath))
        {
            return (overrides, null);
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Provide --config=<path>.");
        }

        configPath = Path.GetFullPath(configPath);
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The configuration must be a JSON object.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!Keys.Contains(property.Name))
            {
                throw new ArgumentException($"Unknown configuration setting: {property.Name}.");
            }

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => throw new ArgumentException($"Configuration setting '{property.Name}' must be a string or number.")
            };
            if (!values.TryAdd(property.Name, value))
            {
                throw new ArgumentException($"Duplicate configuration setting: {property.Name}.");
            }
        }

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return (values, Path.GetDirectoryName(configPath));
    }
}
