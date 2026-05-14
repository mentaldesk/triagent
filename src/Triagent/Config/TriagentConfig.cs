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

    /// <summary>
    /// Path this config was loaded from. <see cref="Save()"/> writes back here.
    /// Set by <see cref="Load(string?)"/> / <see cref="Save(TriagentConfig, string)"/>.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonIgnore]
    public string ResolvedDbPath => ExpandHome(DbPath);

    public static string DefaultConfigPath =>
        Path.Combine(GetHomeDirectory(), ".triagent", "config.json");

    public static TriagentConfig Load(string? path = null)
    {
        var resolved = path ?? DefaultConfigPath;

        if (!File.Exists(resolved))
        {
            var defaults = new TriagentConfig();
            Save(defaults, resolved);
            return defaults;
        }

        var json = File.ReadAllText(resolved);
        var config = JsonSerializer.Deserialize(json, JsonContext.Default.TriagentConfig)
            ?? new TriagentConfig();
        config.SourcePath = resolved;
        return config;
    }

    /// <summary>
    /// Saves this config back to <see cref="SourcePath"/>.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(SourcePath))
            throw new InvalidOperationException(
                "TriagentConfig has no SourcePath. Use Load() first or call Save(config, path).");
        Save(this, SourcePath);
    }

    /// <summary>
    /// Saves <paramref name="config"/> to an explicit path and records it on <see cref="SourcePath"/>.
    /// </summary>
    public static void Save(TriagentConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonContext.Default.TriagentConfig);
        File.WriteAllText(path, json);
        config.SourcePath = path;
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
