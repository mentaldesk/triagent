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

    [JsonIgnore]
    public string ResolvedDbPath => ExpandHome(DbPath);

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".triagent");

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
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.TrimStart('~').TrimStart('/', '\\'));
        }
        return path;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TriagentConfig))]
internal partial class JsonContext : JsonSerializerContext;
