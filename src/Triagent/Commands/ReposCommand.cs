using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class ReposCommand
{
    public static async Task<int> RunAsync(TriagentConfig config, CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var repos = await db.ListReposAsync(ct);
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No repos tracked yet. Run [bold]triagent analyze <owner/repo>[/] first.[/]");
            return 0;
        }
        foreach (var r in repos) AnsiConsole.WriteLine(r);
        return 0;
    }
}
