using System.ComponentModel;
using System.Diagnostics;
using Octokit;
using Sentry;
using Spectre.Console;
using Triagent.Config;
using Triagent.Data;
using Triagent.GitHub;
using Triagent.Models;
using Triagent.Scoring;

namespace Triagent.Commands;

public static class AnalyzeCommand
{
    public static async Task<int> RunAsync(
        string ownerRepo,
        string scorerName,
        string model,
        int batchSize,
        bool force,
        TriagentConfig config,
        CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("analyze", "triagent.analyze");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.Transaction = tx;
            scope.SetTag("repo", ownerRepo);
            scope.SetTag("scorer", scorerName);
            scope.SetTag("model", model);
            scope.SetTag("batch_size", batchSize.ToString());
            scope.SetTag("force", force.ToString());
        });

        try
        {
            var (owner, repo) = SplitRepo(ownerRepo);

            IScorer scorer = scorerName.ToLowerInvariant() switch
            {
                "claude" => new ClaudeCliScorer(model),
                "codex" => new CodexCliScorer(model),
                _ => throw new ArgumentException($"Unknown scorer '{scorerName}'. Use claude or codex.")
            };

            var preflightScorerSpan = tx.StartChild("preflight", "Validate scorer CLI");
            EnsureScorerCliAvailable(scorer.Name);
            preflightScorerSpan.Finish(SpanStatus.Ok);

            var gh = new GitHub.GitHubClient();
            var preflightAuthSpan = tx.StartChild("preflight", "Validate GitHub auth");
            await EnsureGitHubAuthAsync(gh, ct);
            preflightAuthSpan.Finish(SpanStatus.Ok);

            await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);

            AnsiConsole.MarkupLine($"[bold]Triagent[/] analyzing [yellow]{ownerRepo}[/] with scorer [green]{scorer.Name}[/]");

            IReadOnlyList<GitHubIssueSummary> openIssues;
            var fetchOpenIssuesSpan = tx.StartChild("github.fetch_open_issues", "List open issues");
            openIssues = await AnsiConsole.Status()
                .StartAsync("Listing open issues...", _ => gh.GetAllOpenIssuesAsync(owner, repo, ct));
            fetchOpenIssuesSpan.Finish(SpanStatus.Ok);

            AnsiConsole.MarkupLine($"  Found [cyan]{openIssues.Count}[/] open issues");

            IReadOnlyList<string> linkedRepoScope;
            var resolveLinkedScopeSpan = tx.StartChild("resolve.linked_repo_scope", "Resolve linked PR repo scope");
            linkedRepoScope = await ResolveLinkedRepoScopeAsync(ownerRepo, config, db, ct);
            resolveLinkedScopeSpan.Finish(SpanStatus.Ok);

            if (linkedRepoScope.Count > 0)
                AnsiConsole.MarkupLine($"  Linked PR repo scope: [grey]{string.Join(", ", linkedRepoScope)}[/]");

            HashSet<int> linkedToOpenPrs;
            var linkedPrSpan = tx.StartChild("github.fetch_linked_prs", "Find issues linked to open PRs");
            linkedToOpenPrs = await AnsiConsole.Status()
                .StartAsync("Checking issue timeline for associated open PRs...",
                    _ => gh.GetIssuesLinkedToOpenPullRequestsAsync(owner, repo, openIssues.Select(i => i.Number).ToList(), linkedRepoScope, ct));
            linkedPrSpan.Finish(SpanStatus.Ok);

            AnsiConsole.MarkupLine($"  [cyan]{linkedToOpenPrs.Count}[/] issues linked from open PRs");

            IReadOnlyDictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)> dbState;
            var syncStateSpan = tx.StartChild("db.sync_state", "Load sync state");
            dbState = await db.GetSyncStateAsync(ownerRepo, ct);
            syncStateSpan.Finish(SpanStatus.Ok);

            var openOnGitHubNumbers = openIssues.Select(i => i.Number).ToHashSet();
            var newlyClosed = dbState
                .Where(kv => kv.Value.State == "open" && !openOnGitHubNumbers.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();

            var upsertSpan = tx.StartChild("db.upsert_summaries", "Upsert issue summaries");
            foreach (var issue in openIssues)
                await db.UpsertSummaryAsync(ownerRepo, issue, linkedToOpenPrs.Contains(issue.Number), issue.IsAssigned, ct);
            upsertSpan.Finish(SpanStatus.Ok);

            var archiveSpan = tx.StartChild("db.archive_closed", "Archive newly closed issues");
            await db.ArchiveClosedAsync(ownerRepo, newlyClosed, ct);
            archiveSpan.Finish(SpanStatus.Ok);

            var toScore = openIssues
                .Where(i => !linkedToOpenPrs.Contains(i.Number))
                .Where(i => force || NeedsScoring(i, dbState))
                .ToList();

            var skippedPrLinked = openIssues.Count(i => linkedToOpenPrs.Contains(i.Number));
            var skippedUnchanged = openIssues.Count - toScore.Count - skippedPrLinked;

            SentrySdk.ConfigureScope(scope =>
            {
                scope.SetTag("open_issues", openIssues.Count.ToString());
                scope.SetTag("linked_pr_issues", linkedToOpenPrs.Count.ToString());
                scope.SetTag("to_score", toScore.Count.ToString());
                scope.SetTag("newly_closed", newlyClosed.Count.ToString());
            });

            AnsiConsole.MarkupLine($"  [cyan]{toScore.Count}[/] need scoring, [grey]{skippedUnchanged}[/] unchanged, [grey]{skippedPrLinked}[/] PR-linked, [yellow]{newlyClosed.Count}[/] newly closed");

            if (toScore.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]Nothing to score.[/]");
                tx.Finish(SpanStatus.Ok);
                return 0;
            }

            var issuesForScoring = new List<IssueForScoring>(toScore.Count);
            var detailSpan = tx.StartChild("github.fetch_issue_details", "Fetch issue bodies and comments");
            await AnsiConsole.Progress()
                .StartAsync(async progress =>
                {
                    var task = progress.AddTask("Fetching issue bodies + comments", maxValue: toScore.Count);
                    foreach (var summary in toScore)
                    {
                        var (body, comments) = await gh.GetIssueDetailAsync(owner, repo, summary.Number, ct);
                        issuesForScoring.Add(new IssueForScoring(summary.Number, summary.Title, summary.Labels, body, comments));
                        task.Increment(1);
                    }
                });
            detailSpan.Finish(SpanStatus.Ok);

            var batches = issuesForScoring
                .Select((issue, idx) => (issue, idx))
                .GroupBy(t => t.idx / batchSize)
                .Select(g => g.Select(t => t.issue).ToList())
                .ToList();

            var scored = 0;
            var failed = 0;
            var scoringSpan = tx.StartChild("scoring.batches", "Score issue batches");
            await AnsiConsole.Progress()
                .StartAsync(async progress =>
                {
                    var task = progress.AddTask($"Scoring with {scorer.Name}", maxValue: batches.Count);
                    for (var i = 0; i < batches.Count; i++)
                    {
                        var batch = batches[i];
                        var numbers = string.Join(",", batch.Select(b => b.Number));
                        task.Description = $"Scoring with {scorer.Name} [grey](batch {i + 1}/{batches.Count}: {numbers})[/]";

                        var batchSpan = tx.StartChild("scoring.batch", $"Batch {i + 1}/{batches.Count}");
                        batchSpan.SetTag("batch.index", (i + 1).ToString());
                        batchSpan.SetTag("batch.size", batch.Count.ToString());
                        batchSpan.SetData("issue_numbers", numbers);

                        try
                        {
                            var scores = await scorer.ScoreBatchAsync(batch, ct);
                            var byNumber = scores.ToDictionary(s => s.Number);
                            foreach (var issue in batch)
                            {
                                if (byNumber.TryGetValue(issue.Number, out var score))
                                {
                                    await db.SaveScoreAsync(ownerRepo, score, scorer.Name, ct);
                                    scored++;
                                }
                                else
                                {
                                    failed++;
                                }
                            }

                            batchSpan.SetTag("scored", scores.Count.ToString());
                            batchSpan.Finish(SpanStatus.Ok);
                        }
                        catch (Exception ex)
                        {
                            batchSpan.SetTag("error", "true");
                            batchSpan.Finish(SpanStatus.InternalError);

                            SentrySdk.ConfigureScope(scope =>
                            {
                                scope.SetTag("batch.index", (i + 1).ToString());
                                scope.SetExtra("batch.issue_numbers", numbers);
                                scope.SetExtra("batch.issue_count", batch.Count);
                            });
                            SentrySdk.CaptureException(ex);

                            AnsiConsole.MarkupLineInterpolated($"  [red]Batch {i + 1} failed:[/] {ex.Message}");
                            failed += batch.Count;
                        }

                        task.Increment(1);
                    }
                });
            scoringSpan.Finish(SpanStatus.Ok);

            AnsiConsole.MarkupLine($"[green]Done.[/] scored=[cyan]{scored}[/] failed=[red]{failed}[/] unchanged=[grey]{skippedUnchanged}[/] pr-linked=[grey]{skippedPrLinked}[/] newly-closed=[yellow]{newlyClosed.Count}[/]");
            tx.Finish(failed == 0 ? SpanStatus.Ok : SpanStatus.InternalError);
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            tx.Finish(SpanStatus.InternalError);
            SentrySdk.CaptureException(ex);
            throw;
        }
    }

    private static async Task<IReadOnlyList<string>> ResolveLinkedRepoScopeAsync(
        string ownerRepo,
        TriagentConfig config,
        IssueDb db,
        CancellationToken ct)
    {
        var dbScoped = await db.GetLinkedPrReposAsync(ownerRepo, ct);
        if (dbScoped.Count > 0)
            return BuildLinkedRepoScope(ownerRepo, dbScoped);

        if (config.LinkedPrRepos.Count > 0)
        {
            await db.SetLinkedPrReposAsync(ownerRepo, config.LinkedPrRepos, ct);
            return BuildLinkedRepoScope(ownerRepo, config.LinkedPrRepos);
        }

        return BuildLinkedRepoScope(ownerRepo, Array.Empty<string>());
    }

    private static IReadOnlyList<string> BuildLinkedRepoScope(string ownerRepo, IReadOnlyCollection<string> configuredLinkedRepos)
    {
        var scope = new List<string> { ownerRepo };
        foreach (var linked in configuredLinkedRepos)
        {
            if (string.IsNullOrWhiteSpace(linked)) continue;
            var trimmed = linked.Trim();
            if (scope.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                continue;
            scope.Add(trimmed);
        }

        return scope;
    }

    private static bool NeedsScoring(
        GitHubIssueSummary issue,
        IReadOnlyDictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)> dbState)
    {
        if (!dbState.TryGetValue(issue.Number, out var existing)) return true;
        if (existing.LastScoredAt is null) return true;
        return issue.UpdatedAt > existing.LastScoredAt.Value;
    }

    private static (string Owner, string Repo) SplitRepo(string ownerRepo)
    {
        var parts = ownerRepo.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"repo must be in the form owner/name, got '{ownerRepo}'");
        return (parts[0], parts[1]);
    }

    private static async Task EnsureGitHubAuthAsync(GitHub.GitHubClient gh, CancellationToken ct)
    {
        try
        {
            // Lightweight call to validate authenticated GitHub access before full analyze.
            await gh.GetAllOpenIssuesAsync("octokit", "octokit.net", ct);
        }
        catch (Octokit.RateLimitExceededException ex)
        {
            throw new InvalidOperationException(
                "GitHub API rate limit exceeded before analysis started. This usually means unauthenticated requests. " +
                "Set GITHUB_TOKEN or run `gh auth login` so Triagent can use authenticated GitHub API requests.", ex);
        }
        catch (Octokit.AuthorizationException ex)
        {
            throw new InvalidOperationException(
                "GitHub authentication failed. Set a valid GITHUB_TOKEN or run `gh auth login`.", ex);
        }
    }

    private static void EnsureScorerCliAvailable(string scorerName)
    {
        var executable = scorerName.ToLowerInvariant() switch
        {
            "claude" => "claude",
            "codex" => "codex",
            _ => scorerName,
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--version");

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{executable}'.");

            proc.WaitForExit(3000);

            if (proc.HasExited && proc.ExitCode == 0)
                return;

            var stderr = proc.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(
                $"{executable} is installed but not runnable (exit {proc.ExitCode}). {stderr}".Trim());
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Scorer CLI '{executable}' is not available. Install it, ensure it's on PATH, and authenticate it before running analyze with --scorer {scorerName}.",
                ex);
        }
    }
}
