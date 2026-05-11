using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class StatsCommand
{
    public static async Task<int> RunAsync(string? repo, bool includeClosed, TriagentConfig config, CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var rows = await db.AllAsync(repo, includeClosed, ct);

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
        return 0;
    }
}
