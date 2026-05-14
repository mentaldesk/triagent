using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class TopCommand
{
    public static async Task<int> RunAsync(
        int limit,
        string? repo,
        string state,
        int? minScore,
        string? label,
        bool excludeAssigned,
        TriagentConfig config,
        CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("top", "triagent.top");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(all)");
            scope.SetTag("state", state);
            scope.SetTag("limit", limit.ToString());
            scope.SetTag("exclude_assigned", excludeAssigned.ToString());
        });

        try
        {
            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var querySpan = tx.StartChild("db.query_top", "Query top issues");
            var rows = await db.QueryTopAsync(repo, state, limit, minScore, label, excludeAssigned, ct);
            querySpan.Finish(SpanStatus.Ok);

            tx.SetData("rows", rows.Count);

            if (rows.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No issues match.[/]");
                tx.Finish(SpanStatus.Ok);
                return 0;
            }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Repo");
        table.AddColumn(new TableColumn("#").RightAligned());
        table.AddColumn(new TableColumn("Total").RightAligned());
        table.AddColumn("S");
        table.AddColumn("C");
        table.AddColumn("V");
        table.AddColumn("Cl");
        table.AddColumn("SS");
        table.AddColumn("Title");

        foreach (var r in rows)
        {
            table.AddRow(
                r.Repo,
                r.Number.ToString(),
                $"[bold]{r.Total}[/]",
                r.Specificity?.ToString() ?? "-",
                r.Containment?.ToString() ?? "-",
                r.Verifiability?.ToString() ?? "-",
                r.Clarity?.ToString() ?? "-",
                r.SelfSufficiency?.ToString() ?? "-",
                Markup.Escape(r.Title));
        }

            AnsiConsole.Write(table);
            tx.Finish(SpanStatus.Ok);
            return 0;
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
    }
}
