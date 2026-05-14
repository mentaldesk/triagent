using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class StatsCommand
{
    public static async Task<int> RunAsync(string? repo, bool includeClosed, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("stats", "triagent.stats");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(all)");
            scope.SetTag("include_closed", includeClosed.ToString());
        });

        try
        {
            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var querySpan = tx.StartChild("db.all", "Query stats rows");
            var rows = await db.AllAsync(repo, includeClosed, ct);
            querySpan.Finish(SpanStatus.Ok);

            tx.SetData("rows", rows.Count);

            var scored = rows.Where(r => r.Total is not null).ToList();
        var greenLight = scored.Count(r => r.Total >= 13);
        var amber = scored.Count(r => r.Total is >= 9 and <= 12);
        var red = scored.Count(r => r.Total is >= 5 and <= 8);
        var linked = rows.Count(r => r.HasLinkedPr);

        var table = new Table().Border(TableBorder.Rounded).Title("Triagent stats");
        table.AddColumn("Metric");
        table.AddColumn(new TableColumn("Count").RightAligned());
        table.AddRow("Total rows", rows.Count.ToString());
        table.AddRow("Scored", scored.Count.ToString());
        table.AddRow("[green]Agent-ready (13-15)[/]", greenLight.ToString());
        table.AddRow("[yellow]Needs clarification (9-12)[/]", amber.ToString());
        table.AddRow("[red]Human triage first (5-8)[/]", red.ToString());
            table.AddRow("Linked to open PR", linked.ToString());
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
