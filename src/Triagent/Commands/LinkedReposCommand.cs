using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class LinkedReposCommand
{
    public static async Task<int> ListAsync(string? repo, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("linked_repos.list", "triagent.linked-repos.list");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(null)");
        });

        try
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
                tx.Finish(SpanStatus.InvalidArgument);
                return 1;
            }

            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var getSpan = tx.StartChild("db.get_linked_repos", "Load linked PR repos");
            var linked = await db.GetLinkedPrReposAsync(repo, ct);
            getSpan.Finish(SpanStatus.Ok);

            tx.SetData("linked_repo_count", linked.Count);

            AnsiConsole.MarkupLine($"[bold]{repo}[/] linked PR repos:");
            if (linked.Count == 0)
            {
                AnsiConsole.MarkupLine("  [grey](none)[/]");
                tx.Finish(SpanStatus.Ok);
                return 0;
            }

            foreach (var r in linked)
                AnsiConsole.MarkupLine($"  - [cyan]{Markup.Escape(r)}[/]");

            tx.Finish(SpanStatus.Ok);
            return 0;
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
    }

    public static async Task<int> SetAsync(string? repo, IReadOnlyCollection<string> linkedRepos, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("linked_repos.set", "triagent.linked-repos.set");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(null)");
        });

        try
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
                tx.Finish(SpanStatus.InvalidArgument);
                return 1;
            }

            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var setSpan = tx.StartChild("db.set_linked_repos", "Persist linked PR repos");
            await db.SetLinkedPrReposAsync(repo, linkedRepos, ct);
            setSpan.Finish(SpanStatus.Ok);

            tx.SetData("linked_repo_input_count", linkedRepos.Count);
            tx.Finish(SpanStatus.Ok);
            return await ListAsync(repo, config, ct);
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
    }

    public static async Task<int> AddAsync(string? repo, IReadOnlyCollection<string> reposToAdd, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("linked_repos.add", "triagent.linked-repos.add");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(null)");
        });

        try
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
                tx.Finish(SpanStatus.InvalidArgument);
                return 1;
            }

            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var getSpan = tx.StartChild("db.get_linked_repos", "Load linked PR repos");
            var current = await db.GetLinkedPrReposAsync(repo, ct);
            getSpan.Finish(SpanStatus.Ok);

            var merged = current
                .Concat(reposToAdd)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var setSpan = tx.StartChild("db.set_linked_repos", "Persist linked PR repos");
            await db.SetLinkedPrReposAsync(repo, merged, ct);
            setSpan.Finish(SpanStatus.Ok);

            tx.SetData("add_count", reposToAdd.Count);
            tx.SetData("result_count", merged.Count);
            tx.Finish(SpanStatus.Ok);
            return await ListAsync(repo, config, ct);
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
    }

    public static async Task<int> RemoveAsync(string? repo, IReadOnlyCollection<string> reposToRemove, TriagentConfig config, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("linked_repos.remove", "triagent.linked-repos.remove");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", repo ?? "(null)");
        });

        try
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
                tx.Finish(SpanStatus.InvalidArgument);
                return 1;
            }

            var removeSet = reposToRemove
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dbSpan = tx.StartChild("db.open", "Open issue DB");
            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
            dbSpan.Finish(SpanStatus.Ok);

            var getSpan = tx.StartChild("db.get_linked_repos", "Load linked PR repos");
            var current = await db.GetLinkedPrReposAsync(repo, ct);
            getSpan.Finish(SpanStatus.Ok);

            var kept = current.Where(x => !removeSet.Contains(x)).ToList();

            var setSpan = tx.StartChild("db.set_linked_repos", "Persist linked PR repos");
            await db.SetLinkedPrReposAsync(repo, kept, ct);
            setSpan.Finish(SpanStatus.Ok);

            tx.SetData("remove_count", removeSet.Count);
            tx.SetData("result_count", kept.Count);
            tx.Finish(SpanStatus.Ok);
            return await ListAsync(repo, config, ct);
        }
        catch (Exception ex)
        {
            tx.Finish(ex);
            throw;
        }
    }
}
