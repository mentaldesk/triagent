using System.Diagnostics;
using Octokit;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using Sentry;
using Triagent.Models;
using static Octokit.GraphQL.Variable;

namespace Triagent.GitHub;

public sealed class GitHubClient
{
    private readonly Octokit.GitHubClient _client;
    private readonly Octokit.GraphQL.Connection? _graphql;

    public GitHubClient()
    {
        _client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("triagent"));
        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
        {
            _client.Credentials = new Credentials(token);
            _graphql = new Octokit.GraphQL.Connection(
                new Octokit.GraphQL.ProductHeaderValue("triagent"), token);
        }
    }

    private static string? ResolveToken()
    {
        var env = System.Environment.GetEnvironmentVariable("GITHUB_TOKEN");
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
        if (_graphql is null)
            throw new InvalidOperationException("GitHub token is required to query linked PRs.");

        var sourceRepos = (linkedRepoScope ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceRepos.Count == 0)
            sourceRepos.Add($"{owner}/{repo}");

        var targetRepo = $"{owner}/{repo}";
        var openIssueSet = issueNumbers.ToHashSet();
        var linked = new HashSet<int>();

        foreach (var prRepoFull in sourceRepos)
        {
            var parts = prRepoFull.Split('/', 2);
            if (parts.Length != 2) continue;
            var prOwner = parts[0];
            var prName = parts[1];

            await FetchLinkedFromRepoAsync(prOwner, prName, targetRepo, openIssueSet, linked, ct);
        }

        return linked;
    }

    private async Task FetchLinkedFromRepoAsync(
        string prOwner,
        string prName,
        string targetRepo,
        HashSet<int> openIssueSet,
        HashSet<int> linked,
        CancellationToken ct)
    {
        var query = new Query()
            .Repository(name: Var("name"), owner: Var("owner"))
            .PullRequests(first: 100, after: Var("after"), states: new[] { PullRequestState.Open })
            .Select(prs => new Page
            {
                HasNextPage = prs.PageInfo.HasNextPage,
                EndCursor = prs.PageInfo.EndCursor,
                Prs = prs.Nodes.Select(pr => new PrRefs
                {
                    Refs = pr.ClosingIssuesReferences(first: 50, after: null, last: null, before: null, orderBy: null, userLinkedOnly: null)
                              .Nodes
                              .Select(i => new IssueRef
                              {
                                  Number = i.Number,
                                  NameWithOwner = i.Repository.NameWithOwner,
                              }).ToList(),
                }).ToList(),
            })
            .Compile();

        var vars = new Dictionary<string, object?>
        {
            ["owner"] = prOwner,
            ["name"] = prName,
            ["after"] = null,
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _graphql!.Run(query, vars, cancellationToken: ct);

            foreach (var pr in page.Prs)
            {
                foreach (var r in pr.Refs)
                {
                    if (!string.Equals(r.NameWithOwner, targetRepo, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!openIssueSet.Contains(r.Number)) continue;
                    linked.Add(r.Number);
                }
            }

            if (!page.HasNextPage || string.IsNullOrEmpty(page.EndCursor)) break;
            vars["after"] = page.EndCursor;
        }
    }

    private sealed class Page
    {
        public bool HasNextPage { get; set; }
        public string? EndCursor { get; set; }
        public List<PrRefs> Prs { get; set; } = new();
    }

    private sealed class PrRefs
    {
        public List<IssueRef> Refs { get; set; } = new();
    }

    private sealed class IssueRef
    {
        public int Number { get; set; }
        public string NameWithOwner { get; set; } = "";
    }
}
