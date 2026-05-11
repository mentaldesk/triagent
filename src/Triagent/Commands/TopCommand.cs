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
        TriagentConfig config,
        CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var rows = await db.QueryTopAsync(repo, state, limit, minScore, label, ct);

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No issues match.[/]");
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
        return 0;
    }
}
