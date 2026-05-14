using System.Reflection;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class AnalyzeCommandTests
{
    [Fact]
    public void NeedsScoring_WhenIssueNotInDb_ReturnsTrue()
    {
        var issue = new GitHubIssueSummary(
            13,
            "Test",
            "https://example/13",
            "",
            DateTimeOffset.UtcNow,
            false);

        var dbState = new Dictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)>();

        var result = InvokeNeedsScoring(issue, dbState);

        Assert.True(result);
    }

    [Fact]
    public void NeedsScoring_WhenNeverScored_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var issue = new GitHubIssueSummary(13, "Test", "https://example/13", "", now, false);

        var dbState = new Dictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)>
        {
            [13] = ("open", null, now.AddMinutes(-5))
        };

        var result = InvokeNeedsScoring(issue, dbState);

        Assert.True(result);
    }

    [Fact]
    public void NeedsScoring_WhenIssueUpdatedAfterLastScore_ReturnsTrue()
    {
        var lastScore = DateTimeOffset.UtcNow.AddHours(-1);
        var issue = new GitHubIssueSummary(13, "Test", "https://example/13", "", lastScore.AddMinutes(1), false);

        var dbState = new Dictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)>
        {
            [13] = ("open", lastScore, lastScore.AddMinutes(-1))
        };

        var result = InvokeNeedsScoring(issue, dbState);

        Assert.True(result);
    }

    [Fact]
    public void NeedsScoring_WhenIssueUnchangedSinceLastScore_ReturnsFalse()
    {
        var lastScore = DateTimeOffset.UtcNow;
        var issue = new GitHubIssueSummary(13, "Test", "https://example/13", "", lastScore.AddMinutes(-1), false);

        var dbState = new Dictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)>
        {
            [13] = ("open", lastScore, lastScore.AddMinutes(-1))
        };

        var result = InvokeNeedsScoring(issue, dbState);

        Assert.False(result);
    }

    [Fact]
    public async Task ResolveLinkedRepoScope_WhenDbHasRepoScopedSettings_UsesDbAndIgnoresGlobalConfig()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await IssueDb.OpenAsync(dbPath);
            const string repo = "owner/target";
            await db.SetLinkedPrReposAsync(repo, new[] { "owner/docs" });

            var cfg = new TriagentConfig
            {
                LinkedPrRepos = new List<string> { "owner/legacy-global" }
            };

            var scope = await InvokeResolveLinkedRepoScope(repo, cfg, db);

            Assert.Equal(new[] { "owner/target", "owner/docs" }, scope);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task ResolveLinkedRepoScope_WhenDbEmptyAndGlobalSet_MigratesGlobalToRepoScoped()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await IssueDb.OpenAsync(dbPath);
            const string repo = "owner/target";

            var cfg = new TriagentConfig
            {
                LinkedPrRepos = new List<string> { "owner/docs", "owner/docs", "owner/tools" }
            };

            var scope = await InvokeResolveLinkedRepoScope(repo, cfg, db);

            Assert.Equal(new[] { "owner/target", "owner/docs", "owner/tools" }, scope);

            var persisted = await db.GetLinkedPrReposAsync(repo);
            Assert.Equal(new[] { "owner/docs", "owner/tools" }, persisted.OrderBy(x => x).ToArray());
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static bool InvokeNeedsScoring(
        GitHubIssueSummary issue,
        IReadOnlyDictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)> dbState)
    {
        var t = typeof(Triagent.Commands.AnalyzeCommand);
        var m = t.GetMethod("NeedsScoring", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NeedsScoring not found.");

        var value = m.Invoke(null, [issue, dbState]);
        return value as bool? ?? throw new InvalidOperationException("Unexpected return type.");
    }

    private static async Task<IReadOnlyList<string>> InvokeResolveLinkedRepoScope(string repo, TriagentConfig config, IssueDb db)
    {
        var t = typeof(Triagent.Commands.AnalyzeCommand);
        var m = t.GetMethod("ResolveLinkedRepoScopeAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveLinkedRepoScopeAsync not found.");

        var task = m.Invoke(null, [repo, config, db, CancellationToken.None]) as Task<IReadOnlyList<string>>;
        if (task is null) throw new InvalidOperationException("Unexpected return type from ResolveLinkedRepoScopeAsync.");
        return await task;
    }
}
