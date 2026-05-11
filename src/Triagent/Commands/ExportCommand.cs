using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;

namespace Triagent.Commands;

public static class ExportCommand
{
    public static async Task<int> RunAsync(
        string? repo,
        string format,
        string? output,
        bool includeClosed,
        TriagentConfig config,
        CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var rows = await db.AllAsync(repo, includeClosed, ct);

        var content = format.ToLowerInvariant() switch
        {
            "tsv" => ToDelimited(rows, '\t'),
            "csv" => ToDelimited(rows, ','),
            "json" => JsonSerializer.Serialize(rows, ExportJsonContext.Default.IReadOnlyListIssueRow),
            _ => throw new ArgumentException($"Unknown --format '{format}'. Use tsv, csv, or json.")
        };

        if (output is null)
        {
            Console.Write(content);
        }
        else
        {
            await File.WriteAllTextAsync(output, content, ct);
            AnsiConsole.MarkupLine($"[green]Wrote {rows.Count} rows to {output}[/]");
        }
        return 0;
    }

    private static string ToDelimited(IReadOnlyList<IssueRow> rows, char sep)
    {
        var sb = new StringBuilder();
        string[] headers =
        [
            "repo", "number", "title", "url", "labels", "state", "has_linked_pr",
            "github_updated_at", "closed_at", "last_scored_at", "scorer",
            "specificity", "containment", "verifiability", "clarity", "self_sufficiency",
            "total", "rationale"
        ];
        sb.AppendLine(string.Join(sep, headers));

        foreach (var r in rows)
        {
            string[] fields =
            [
                r.Repo,
                r.Number.ToString(),
                Quote(r.Title, sep),
                r.Url,
                Quote(r.Labels, sep),
                r.State,
                r.HasLinkedPr ? "1" : "0",
                r.GitHubUpdatedAt.ToString("o"),
                r.ClosedAt?.ToString("o") ?? "",
                r.LastScoredAt?.ToString("o") ?? "",
                r.Scorer ?? "",
                r.Specificity?.ToString() ?? "",
                r.Containment?.ToString() ?? "",
                r.Verifiability?.ToString() ?? "",
                r.Clarity?.ToString() ?? "",
                r.SelfSufficiency?.ToString() ?? "",
                r.Total?.ToString() ?? "",
                Quote(r.Rationale ?? "", sep),
            ];
            sb.AppendLine(string.Join(sep, fields));
        }
        return sb.ToString();
    }

    private static string Quote(string s, char sep)
    {
        // TSV: replace tabs/newlines with spaces. CSV: quote and escape "
        if (sep == '\t')
            return s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        return $"\"{s.Replace("\"", "\"\"")}\"";
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<IssueRow>))]
internal partial class ExportJsonContext : JsonSerializerContext;
