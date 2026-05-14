using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Data;

public sealed class IssueDbReviewDecisionTests
{
    [Fact]
    public async Task SaveReviewDecision_Skip_RequiresReason()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", issue, hasLinkedPr: false, isAssigned: false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            db.SaveReviewDecisionAsync("mentaldesk/triagent", 13, ReviewDecision.Skip, null));

        Assert.Equal("reason", ex.ParamName);
    }

    [Fact]
    public async Task SaveReviewDecision_PersistsTryAndSkip()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", issue, hasLinkedPr: false, isAssigned: false);
        await db.SaveScoreAsync("mentaldesk/triagent", new IssueScore(13, 4, 4, 4, 4, 4, "clear task"), "claude");

        await db.SaveReviewDecisionAsync("mentaldesk/triagent", 13, ReviewDecision.Try, null);
        var rowAfterTry = await db.GetAsync("mentaldesk/triagent", 13);
        Assert.NotNull(rowAfterTry);
        Assert.Equal(ReviewDecision.Try, rowAfterTry!.ReviewDecision);
        Assert.Null(rowAfterTry.ReviewReason);
        Assert.NotNull(rowAfterTry.DecidedAt);

        await db.SaveReviewDecisionAsync("mentaldesk/triagent", 13, ReviewDecision.Skip, "Too risky right now");
        var rowAfterSkip = await db.GetAsync("mentaldesk/triagent", 13);
        Assert.NotNull(rowAfterSkip);
        Assert.Equal(ReviewDecision.Skip, rowAfterSkip!.ReviewDecision);
        Assert.Equal("Too risky right now", rowAfterSkip.ReviewReason);
        Assert.NotNull(rowAfterSkip.DecidedAt);
    }

    [Fact]
    public async Task SaveReviewDecision_None_ClearsDecisionAndReason()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", issue, hasLinkedPr: false, isAssigned: false);

        await db.SaveReviewDecisionAsync("mentaldesk/triagent", 13, ReviewDecision.Skip, "not now");
        await db.SaveReviewDecisionAsync("mentaldesk/triagent", 13, ReviewDecision.None, null);

        var row = await db.GetAsync("mentaldesk/triagent", 13);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.None, row!.ReviewDecision);
        Assert.Null(row.ReviewReason);
        Assert.Null(row.DecidedAt);
    }

    [Fact]
    public async Task QueryReviewQueue_ReturnsScoredOpenWithoutLinkedPrOrAssigned_OrderedByScore()
    {
        var dbPath = TempDbPath();
        await using var db = await IssueDb.OpenAsync(dbPath);

        await db.UpsertSummaryAsync("mentaldesk/triagent", new GitHubIssueSummary(1, "A", "https://example/1", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: false, isAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", new GitHubIssueSummary(2, "B", "https://example/2", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: true, isAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", new GitHubIssueSummary(3, "C", "https://example/3", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: false, isAssigned: false);
        await db.UpsertSummaryAsync("mentaldesk/triagent", new GitHubIssueSummary(4, "D", "https://example/4", "", DateTimeOffset.UtcNow, IsAssigned: true), hasLinkedPr: false, isAssigned: true);

        await db.SaveScoreAsync("mentaldesk/triagent", new IssueScore(1, 4, 4, 4, 4, 4, "A"), "claude");
        await db.SaveScoreAsync("mentaldesk/triagent", new IssueScore(3, 2, 2, 2, 2, 2, "C"), "claude");
        await db.SaveScoreAsync("mentaldesk/triagent", new IssueScore(4, 5, 5, 5, 5, 5, "D"), "claude");

        var rows = await db.QueryReviewQueueAsync("mentaldesk/triagent");

        Assert.Collection(rows,
            r => Assert.Equal(1, r.Number),
            r => Assert.Equal(3, r.Number));
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
}
