using System.Diagnostics;
using Octokit;
using Sentry;
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
        var span = SentrySdk.GetSpan()?.StartChild("github.issues.list", "GitHub list open issues");
        span?.SetTag("repo", $"{owner}/{repo}");

        var request = new RepositoryIssueRequest { State = ItemStateFilter.Open };
        var options = new ApiOptions { PageSize = 100 };
        var issues = await _client.Issue.GetAllForRepository(owner, repo, request, options);

        var result = issues
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

        span?.SetData("open_issue_count", result.Count);
        span?.Finish(SpanStatus.Ok);
        return result;
    }

    public async Task<(string Body, IReadOnlyList<string> Comments)> GetIssueDetailAsync(
        string owner, string repo, int number, CancellationToken ct = default)
    {
        var span = SentrySdk.GetSpan()?.StartChild("github.issue.detail", "GitHub fetch issue detail");
        span?.SetTag("repo", $"{owner}/{repo}");
        span?.SetData("issue_number", number);

        var issue = await _client.Issue.Get(owner, repo, number);
        var comments = await _client.Issue.Comment.GetAllForIssue(owner, repo, number);

        span?.SetData("comment_count", comments.Count);
        span?.Finish(SpanStatus.Ok);
        return (issue.Body ?? "", comments.Select(c => c.Body ?? "").ToList());
    }

    public async Task<HashSet<int>> GetIssuesLinkedToOpenPullRequestsAsync(
        string owner,
        string repo,
        IReadOnlyCollection<int> issueNumbers,
        IReadOnlyCollection<string>? linkedRepoScope = null,
        CancellationToken ct = default)
    {
        var linked = new HashSet<int>();
        var allowedRepos = (linkedRepoScope ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowedRepos.Count == 0)
            allowedRepos.Add($"{owner}/{repo}");

        foreach (var issueNumber in issueNumbers)
        {
            var timeline = await _client.Issue.Timeline.GetAllForIssue(owner, repo, issueNumber, new ApiOptions { PageSize = 100 });

            foreach (var ev in timeline)
            {
                if (ev.Event != EventInfoState.Crossreferenced) continue;
                if (ev.Source?.Issue?.PullRequest is null) continue;
                if (ev.Source.Issue.State != ItemState.Open) continue;

                var sourceRepo = TryGetOwnerRepoFromIssueApiUrl(ev.Source.Issue.Url);

                if (sourceRepo is not null && allowedRepos.Contains(sourceRepo))
                {
                    linked.Add(issueNumber);
                    break;
                }
            }
        }

        return linked;
    }

    private static string? TryGetOwnerRepoFromIssueApiUrl(string? issueApiUrl)
    {
        if (string.IsNullOrWhiteSpace(issueApiUrl)) return null;
        if (!Uri.TryCreate(issueApiUrl, UriKind.Absolute, out var uri)) return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Expected: repos/{owner}/{repo}/issues/{number}
        if (segments.Length < 4) return null;
        if (!string.Equals(segments[0], "repos", StringComparison.OrdinalIgnoreCase)) return null;

        var owner = segments[1];
        var repo = segments[2];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return null;

        return $"{owner}/{repo}";
    }
}
