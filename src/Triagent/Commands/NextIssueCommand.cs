using Triagent.Config;
using Triagent.Data;

namespace Triagent.Commands;

public static class NextIssueCommand
{
    public static async Task<int> RunAsync(string? repo, TriagentConfig config, CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var issue = await db.GetNextIssueAsync(repo, ct);
        Console.WriteLine(issue?.Number ?? 0);
        return 0;
    }
}
