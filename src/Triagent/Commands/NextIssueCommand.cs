using System.Text.Json;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;

namespace Triagent.Commands;

public static class NextIssueCommand
{
    public static async Task<int> RunAsync(string? repo, bool all, bool json, TriagentConfig config, CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var issues = await db.QueryTryIssuesAsync(repo, all ? null : 1, ct);
        var output = Render(issues, all, json);
        if (output.Length > 0) Console.WriteLine(output);
        return 0;
    }

    internal static string Render(IReadOnlyList<IssueRow> issues, bool all, bool json)
    {
        if (json) return JsonSerializer.Serialize(issues, ExportJsonContext.Default.IReadOnlyListIssueRow);
        if (all) return string.Join(Environment.NewLine, issues.Select(i => i.Number));
        return (issues.FirstOrDefault()?.Number ?? 0).ToString();
    }
}
