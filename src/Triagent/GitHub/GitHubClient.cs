using System.Diagnostics;
using System.Text.RegularExpressions;
using Octokit;
using Triagent.Models;

namespace Triagent.GitHub;

/// <summary>
/// Thin wrapper around Octokit for the operations Triagent needs. Auth comes from
/// GITHUB_TOKEN if set, else from `gh auth token`.
/// </summary>
public sealed partial class GitHubClient
{
    private readonly Octokit.GitHubClient _client;

    public GitHubClient()
    {
        _client = new Octokit.GitHubClient(new ProductHeaderValue("triagent"));
        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
            _client.Credentials = new Credentials(token);
    }

    private static string? ResolveToken()
    {
        var env = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Streams every open issue in the repo (excludes pull requests).
    /// </summary>
    public async Task<IReadOnlyList<GitHubIssueSummary>> GetAllOpenIssuesAsync(
        string owner, string repo, CancellationToken ct = default)
    {
        var request = new RepositoryIssueRequest { State = ItemStateFilter.Open };
        var options = new ApiOptions { PageSize = 100 };
        var issues = await _client.Issue.GetAllForRepository(owner, repo, request, options);
        return issues
            .Where(i => i.PullRequest is null)
            .Select(i => new GitHubIssueSummary(
                i.Number,
                i.Title,
                i.HtmlUrl,
                string.Join(", ", i.Labels.Select(l => l.Name)),
                i.Assignees.Count > 0,
                string.Join(", ", i.Assignees.Select(a => a.Login)),
                i.UpdatedAt ?? i.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// Fetches the body and all comment bodies for an issue.
    /// </summary>
    public async Task<(string Body, IReadOnlyList<string> Comments)> GetIssueDetailAsync(
        string owner, string repo, int number, CancellationToken ct = default)
    {
        var issue = await _client.Issue.Get(owner, repo, number);
        var comments = await _client.Issue.Comment.GetAllForIssue(owner, repo, number);
        return (issue.Body ?? "", comments.Select(c => c.Body ?? "").ToList());
    }

    /// <summary>
    /// Returns the set of open issue numbers that have at least one open PR referencing
    /// them via a "Closes/Fixes/Resolves #N" keyword in its body, across the provided
    /// repositories. This is a cheap proxy for the official linked-PRs API, which only
    /// exists in GraphQL.
    /// </summary>
    public async Task<HashSet<int>> GetIssuesLinkedToOpenPullRequestsAsync(
        IReadOnlyList<(string Owner, string Repo)> repos,
        CancellationToken ct = default)
    {
        var linked = new HashSet<int>();

        foreach (var (owner, repo) in repos)
        {
            var prs = await _client.PullRequest.GetAllForRepository(owner, repo,
                new PullRequestRequest { State = ItemStateFilter.Open },
                new ApiOptions { PageSize = 100 });

            foreach (var pr in prs)
            {
                var body = pr.Body ?? "";
                foreach (Match m in CloseKeywordRegex().Matches(body))
                {
                    if (int.TryParse(m.Groups["n"].ValueSpan, out var n)) linked.Add(n);
                }
            }
        }

        return linked;
    }

    // GitHub's auto-close keywords: close[s|d], fix[es|ed], resolve[s|d] — followed by #N.
    [GeneratedRegex(@"\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+#(?<n>\d+)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CloseKeywordRegex();
}
