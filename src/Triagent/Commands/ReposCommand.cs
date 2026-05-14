using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class ReposCommand
{
    public static async Task<int> RunAsync(TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("repos", "triagent.repos");
        SentrySdk.ConfigureScope(scope => scope.Transaction = tx);

        try
        {
            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var listSpan = tx.StartChild("db.list_repos", "List tracked repos");
            var repos = await db.ListReposAsync(ct);
            listSpan.Finish(SpanStatus.Ok);

            tx.SetData("repo_count", repos.Count);

            if (repos.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No repos tracked yet. Run [bold]triagent analyze <owner/repo>[/] first.[/]");
                tx.Finish(SpanStatus.Ok);
                return 0;
            }

            foreach (var r in repos) AnsiConsole.WriteLine(r);
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
