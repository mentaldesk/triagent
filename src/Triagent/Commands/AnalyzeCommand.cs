using System.ComponentModel;
using System.Diagnostics;
using Octokit;
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
        var (owner, repo) = SplitRepo(ownerRepo);

        IScorer scorer = scorerName.ToLowerInvariant() switch
        {
            "claude" => new ClaudeCliScorer(model),
            "codex" => new CodexCliScorer(model),
            _ => throw new ArgumentException($"Unknown scorer '{scorerName}'. Use claude or codex.")
        };

        EnsureScorerCliAvailable(scorer.Name);

        var gh = new GitHub.GitHubClient();
        await EnsureGitHubAuthAsync(gh, ct);

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);

        AnsiConsole.MarkupLine($"[bold]Triagent[/] analyzing [yellow]{ownerRepo}[/] with scorer [green]{scorer.Name}[/]");

        var openIssues = await AnsiConsole.Status()
            .StartAsync("Listing open issues...", _ => gh.GetAllOpenIssuesAsync(owner, repo, ct));
        AnsiConsole.MarkupLine($"  Found [cyan]{openIssues.Count}[/] open issues");

        var linkedToOpenPrs = await AnsiConsole.Status()
            .StartAsync("Checking issue timeline for associated open PRs...",
                _ => gh.GetIssuesLinkedToOpenPullRequestsAsync(owner, repo, openIssues.Select(i => i.Number).ToList(), ct));
        AnsiConsole.MarkupLine($"  [cyan]{linkedToOpenPrs.Count}[/] issues linked from open PRs");

        // Snapshot current DB state for sync decisions
        var dbState = await db.GetSyncStateAsync(ownerRepo, ct);

        // Detect newly closed issues: in DB as open but no longer in the open listing
        var openOnGitHubNumbers = openIssues.Select(i => i.Number).ToHashSet();
        var newlyClosed = dbState
            .Where(kv => kv.Value.State == "open" && !openOnGitHubNumbers.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        // Upsert all open issues (including PR-linked ones, so we record them)
        foreach (var issue in openIssues)
            await db.UpsertSummaryAsync(ownerRepo, issue, linkedToOpenPrs.Contains(issue.Number), issue.IsAssigned, ct);

        // Archive issues that no longer appear in the open listing
        await db.ArchiveClosedAsync(ownerRepo, newlyClosed, ct);

        // Decide which open, non-PR-linked issues need (re-)scoring
        var toScore = openIssues
            .Where(i => !linkedToOpenPrs.Contains(i.Number))
            .Where(i => force || NeedsScoring(i, dbState))
            .ToList();

        var skippedPrLinked = openIssues.Count(i => linkedToOpenPrs.Contains(i.Number));
        var skippedUnchanged = openIssues.Count - toScore.Count - skippedPrLinked;

        AnsiConsole.MarkupLine($"  [cyan]{toScore.Count}[/] need scoring, [grey]{skippedUnchanged}[/] unchanged, [grey]{skippedPrLinked}[/] PR-linked, [yellow]{newlyClosed.Count}[/] newly closed");

        if (toScore.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing to score.[/]");
            return 0;
        }

        // Fetch detail (body + comments) for each issue to score
        var issuesForScoring = new List<IssueForScoring>(toScore.Count);
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

        // Batch and score
        var batches = issuesForScoring
            .Select((issue, idx) => (issue, idx))
            .GroupBy(t => t.idx / batchSize)
            .Select(g => g.Select(t => t.issue).ToList())
            .ToList();

        var scored = 0;
        var failed = 0;
        await AnsiConsole.Progress()
            .StartAsync(async progress =>
            {
                var task = progress.AddTask($"Scoring with {scorer.Name}", maxValue: batches.Count);
                for (var i = 0; i < batches.Count; i++)
                {
                    var batch = batches[i];
                    var numbers = string.Join(",", batch.Select(b => b.Number));
                    task.Description = $"Scoring with {scorer.Name} [grey](batch {i + 1}/{batches.Count}: {numbers})[/]";
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
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLineInterpolated($"  [red]Batch {i + 1} failed:[/] {ex.Message}");
                        failed += batch.Count;
                    }
                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]Done.[/] scored=[cyan]{scored}[/] failed=[red]{failed}[/] unchanged=[grey]{skippedUnchanged}[/] pr-linked=[grey]{skippedPrLinked}[/] newly-closed=[yellow]{newlyClosed.Count}[/]");
        return failed == 0 ? 0 : 1;
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
