using System.Diagnostics;
using Octokit;
using Triagent.Models;

namespace Triagent.GitHub;

public sealed class GitHubClient
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
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

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
            var error = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit();

            if (proc.ExitCode != 0) return null;
            if (!string.IsNullOrWhiteSpace(error)) return null;
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

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

    public async Task<(string Body, IReadOnlyList<string> Comments)> GetIssueDetailAsync(
        string owner, string repo, int number, CancellationToken ct = default)
    {
        var issue = await _client.Issue.Get(owner, repo, number);
        var comments = await _client.Issue.Comment.GetAllForIssue(owner, repo, number);
        return (issue.Body ?? "", comments.Select(c => c.Body ?? "").ToList());
    }

    public async Task<HashSet<int>> GetIssuesLinkedToOpenPullRequestsAsync(
        string owner,
        string repo,
        IReadOnlyCollection<int> issueNumbers,
        CancellationToken ct = default)
    {
        var linked = new HashSet<int>();

        foreach (var issueNumber in issueNumbers)
        {
            var timeline = await _client.Issue.Timeline.GetAllForIssue(owner, repo, issueNumber, new ApiOptions { PageSize = 100 });

            foreach (var ev in timeline)
            {
                if (ev.Event != EventInfoState.Crossreferenced) continue;
                if (ev.Source?.Issue?.PullRequest is null) continue;
                if (ev.Source.Issue.State == ItemState.Open)
                {
                    linked.Add(issueNumber);
                    break;
                }
            }
        }

        return linked;
    }
}
