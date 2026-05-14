using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class ShowCommand
{
    public static async Task<int> RunAsync(int number, string? repo, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("show", "triagent.show");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(null)");
            scope.SetTag("number", number.ToString());
        });

        try
        {
            if (repo is null)
            {
                AnsiConsole.MarkupLine("[red]--repo is required (or set defaultRepo in config).[/]");
                tx.Finish(SpanStatus.InvalidArgument);
                return 1;
            }

            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var querySpan = tx.StartChild("db.get_issue", "Get issue row");
            var row = await db.GetAsync(repo, number, ct);
            querySpan.Finish(SpanStatus.Ok);

            if (row is null)
            {
                AnsiConsole.MarkupLine($"[red]No record for {repo}#{number}.[/]");
                tx.Finish(SpanStatus.NotFound);
                return 1;
            }

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[bold]Repo[/]", row.Repo);
        grid.AddRow("[bold]Number[/]", row.Number.ToString());
        grid.AddRow("[bold]Title[/]", Markup.Escape(row.Title));
        grid.AddRow("[bold]URL[/]", row.Url);
        grid.AddRow("[bold]State[/]", row.State);
        grid.AddRow("[bold]Labels[/]", Markup.Escape(row.Labels));
        grid.AddRow("[bold]Has linked PR[/]", row.HasLinkedPr.ToString());
        grid.AddRow("[bold]Has assignees[/]", row.HasAssignees.ToString());
        grid.AddRow("[bold]Assignees[/]", Markup.Escape(string.IsNullOrWhiteSpace(row.Assignees) ? "-" : row.Assignees));
        grid.AddRow("[bold]GitHub updated[/]", row.GitHubUpdatedAt.ToString("u"));
        grid.AddRow("[bold]Last scored[/]", row.LastScoredAt?.ToString("u") ?? "(never)");
        grid.AddRow("[bold]Scorer[/]", row.Scorer ?? "-");
        grid.AddRow("[bold]Total[/]", row.Total?.ToString() ?? "-");
        grid.AddRow("[bold]Specificity[/]", row.Specificity?.ToString() ?? "-");
        grid.AddRow("[bold]Containment[/]", row.Containment?.ToString() ?? "-");
        grid.AddRow("[bold]Verifiability[/]", row.Verifiability?.ToString() ?? "-");
        grid.AddRow("[bold]Clarity[/]", row.Clarity?.ToString() ?? "-");
        grid.AddRow("[bold]Self-sufficiency[/]", row.SelfSufficiency?.ToString() ?? "-");
        grid.AddRow("[bold]Rationale[/]", Markup.Escape(row.Rationale ?? "-"));

            AnsiConsole.Write(grid);
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
