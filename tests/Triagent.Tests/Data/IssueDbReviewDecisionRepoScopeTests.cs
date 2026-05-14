using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Data;

public sealed class IssueDbReviewDecisionRepoScopeTests
{
    [Fact]
    public async Task SaveReviewDecision_UpdatesOnlyMatchingRepoAndNumber()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        await SeedScoredIssue(db, "getsentry/sentry-dotnet", 5058);
        await SeedScoredIssue(db, "mentaldesk/triagent", 5058);

        await db.SaveReviewDecisionAsync("getsentry/sentry-dotnet", 5058, ReviewDecision.Skip, "owned elsewhere");

        var sentry = await db.GetAsync("getsentry/sentry-dotnet", 5058);
        var triagent = await db.GetAsync("mentaldesk/triagent", 5058);

        Assert.NotNull(sentry);
        Assert.Equal(ReviewDecision.Skip, sentry!.ReviewDecision);
        Assert.Equal("owned elsewhere", sentry.ReviewReason);

        Assert.NotNull(triagent);
        Assert.Equal(ReviewDecision.None, triagent!.ReviewDecision);
        Assert.Null(triagent.ReviewReason);
    }

    [Fact]
    public async Task SaveReviewDecision_ThrowsWhenRowDoesNotExist()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        await SeedScoredIssue(db, "getsentry/sentry-dotnet", 5058);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.SaveReviewDecisionAsync("mentaldesk/triagent", 5058, ReviewDecision.Skip, "wrong repo"));

        Assert.Contains("No issue row updated", ex.Message);
    }

    private static async Task SeedScoredIssue(IssueDb db, string repo, int number)
    {
        var issue = new GitHubIssueSummary(number, $"Issue {number}", $"https://example/{number}", "", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(repo, issue, hasLinkedPr: false, isAssigned: false);
        await db.SaveScoreAsync(repo, new IssueScore(number, 3, 3, 3, 3, 3, "ok"), "claude");
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
}
