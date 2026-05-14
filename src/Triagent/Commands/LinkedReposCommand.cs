using Spectre.Console;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class LinkedReposCommand
{
    public static async Task<int> ListAsync(string? repo, TriagentConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
            return 1;
        }

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var linked = await db.GetLinkedPrReposAsync(repo, ct);

        AnsiConsole.MarkupLine($"[bold]{repo}[/] linked PR repos:");
        if (linked.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey](none)[/]");
            return 0;
        }

        foreach (var r in linked)
            AnsiConsole.MarkupLine($"  - [cyan]{Markup.Escape(r)}[/]");
        return 0;
    }

    public static async Task<int> SetAsync(string? repo, IReadOnlyCollection<string> linkedRepos, TriagentConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
            return 1;
        }

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        await db.SetLinkedPrReposAsync(repo, linkedRepos, ct);
        return await ListAsync(repo, config, ct);
    }

    public static async Task<int> AddAsync(string? repo, IReadOnlyCollection<string> reposToAdd, TriagentConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
            return 1;
        }

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var current = await db.GetLinkedPrReposAsync(repo, ct);
        var merged = current
            .Concat(reposToAdd)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await db.SetLinkedPrReposAsync(repo, merged, ct);
        return await ListAsync(repo, config, ct);
    }

    public static async Task<int> RemoveAsync(string? repo, IReadOnlyCollection<string> reposToRemove, TriagentConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
            return 1;
        }

        var removeSet = reposToRemove
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var current = await db.GetLinkedPrReposAsync(repo, ct);
        var kept = current.Where(x => !removeSet.Contains(x)).ToList();

        await db.SetLinkedPrReposAsync(repo, kept, ct);
        return await ListAsync(repo, config, ct);
    }
}
