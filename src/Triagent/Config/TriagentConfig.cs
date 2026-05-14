using System.Text.Json;
using System.Text.Json.Serialization;

namespace Triagent.Config;

public sealed class TriagentConfig
{
    public string? DefaultRepo { get; set; }
    public string DefaultScorer { get; set; } = "claude";
    public string DefaultModel { get; set; } = "sonnet";
    public int BatchSize { get; set; } = 5;
    public string DbPath { get; set; } = "~/.triagent/triagent.db";
    public string? SentryDsn { get; set; }

    /// <summary>
    /// Persisted review filter: all | unreviewed | skip | try.
    /// </summary>
    public string ReviewFilter { get; set; } = "all";

    /// <summary>
    /// Repositories to scan for open PRs that link to issues in the analyzed repo.
    /// Entries must be in owner/name form. If unset/empty, Triagent scans only the
    /// analyzed repository.
    /// </summary>
    public List<string> LinkedPrRepos { get; set; } = new();

    [JsonIgnore]
    public string ResolvedDbPath => ExpandHome(DbPath);

    public static string ConfigDir =>
        Path.Combine(GetHomeDirectory(), ".triagent");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static TriagentConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new TriagentConfig();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize(json, JsonContext.Default.TriagentConfig)
            ?? new TriagentConfig();
    }

    public static void Save(TriagentConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonContext.Default.TriagentConfig);
        File.WriteAllText(ConfigPath, json);
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = GetHomeDirectory();
            return Path.Combine(home, path.TrimStart('~').TrimStart('/', '\\'));
        }
        return path;
    }

    private static string GetHomeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        throw new InvalidOperationException("Unable to resolve user home directory.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TriagentConfig))]
internal partial class JsonContext : JsonSerializerContext;
