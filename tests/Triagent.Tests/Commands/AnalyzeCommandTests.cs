using Triagent.Commands;
using Triagent.Config;
using Triagent.Models;
using Triagent.Tests.Support;
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

        Assert.True(AnalyzeCommand.NeedsScoring(issue, dbState));
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

        Assert.True(AnalyzeCommand.NeedsScoring(issue, dbState));
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

        Assert.True(AnalyzeCommand.NeedsScoring(issue, dbState));
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

        Assert.False(AnalyzeCommand.NeedsScoring(issue, dbState));
    }

    [Fact]
    public async Task ResolveLinkedRepoScope_WhenDbHasRepoScopedSettings_UsesDbAndIgnoresGlobalConfig()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        const string repo = "owner/target";
        await db.SetLinkedPrReposAsync(repo, new[] { "owner/docs" });

        var cfg = new TriagentConfig
        {
            LinkedPrRepos = new List<string> { "owner/legacy-global" }
        };

        var scope = await AnalyzeCommand.ResolveLinkedRepoScopeAsync(repo, cfg, db, CancellationToken.None);

        Assert.Equal(new[] { "owner/target", "owner/docs" }, scope);
    }

    [Fact]
    public async Task ResolveLinkedRepoScope_WhenDbEmptyAndGlobalSet_MigratesGlobalToRepoScoped()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        const string repo = "owner/target";

        var cfg = new TriagentConfig
        {
            LinkedPrRepos = new List<string> { "owner/docs", "owner/docs", "owner/tools" }
        };

        var scope = await AnalyzeCommand.ResolveLinkedRepoScopeAsync(repo, cfg, db, CancellationToken.None);

        Assert.Equal(new[] { "owner/target", "owner/docs", "owner/tools" }, scope);

        var persisted = await db.GetLinkedPrReposAsync(repo);
        Assert.Equal(new[] { "owner/docs", "owner/tools" }, persisted.OrderBy(x => x).ToArray());
    }
}
