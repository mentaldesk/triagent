using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentry;
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
        var tx = SentrySdk.StartTransaction("export", "triagent.export");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(all)");
            scope.SetTag("format", format);
            scope.SetTag("include_closed", includeClosed.ToString());
            scope.SetTag("has_output_file", (output is not null).ToString());
        });

        try
        {
            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var querySpan = tx.StartChild("db.all", "Query export rows");
            var rows = await db.AllAsync(repo, includeClosed, ct);
            querySpan.Finish(SpanStatus.Ok);

            tx.SetData("rows", rows.Count);

            var formatSpan = tx.StartChild("export.format", "Render export payload");
            var content = format.ToLowerInvariant() switch
            {
                "tsv" => ToDelimited(rows, '\t'),
                "csv" => ToDelimited(rows, ','),
                "json" => JsonSerializer.Serialize(rows, ExportJsonContext.Default.IReadOnlyListIssueRow),
                _ => throw new ArgumentException($"Unknown --format '{format}'. Use tsv, csv, or json.")
            };
            formatSpan.Finish(SpanStatus.Ok);

            if (output is null)
            {
                Console.Write(content);
            }
            else
            {
                var writeSpan = tx.StartChild("export.write", "Write export output file");
                await File.WriteAllTextAsync(output, content, ct);
                writeSpan.Finish(SpanStatus.Ok);
                AnsiConsole.MarkupLine($"[green]Wrote {rows.Count} rows to {output}[/]");
            }

            tx.Finish(SpanStatus.Ok);
            return 0;
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
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
