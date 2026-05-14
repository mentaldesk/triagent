using Triagent.Models;
using Triagent.Tests.Support;
using Xunit;

namespace Triagent.Tests.Data;

public sealed class IssueDbReviewDecisionTests
{
    private const string Repo = "mentaldesk/triagent";

    [Fact]
    public async Task SaveReviewDecision_Skip_RequiresReason()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(Repo, issue, hasLinkedPr: false, isAssigned: false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            db.SaveReviewDecisionAsync(Repo, 13, ReviewDecision.Skip, null));

        Assert.Equal("reason", ex.ParamName);
    }

    [Fact]
    public async Task SaveReviewDecision_PersistsTryAndSkip()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(Repo, issue, hasLinkedPr: false, isAssigned: false);
        await db.SaveScoreAsync(Repo, new IssueScore(13, 4, 4, 4, 4, 4, "clear task"), "claude");

        await db.SaveReviewDecisionAsync(Repo, 13, ReviewDecision.Try, null);
        var rowAfterTry = await db.GetAsync(Repo, 13);
        Assert.NotNull(rowAfterTry);
        Assert.Equal(ReviewDecision.Try, rowAfterTry!.ReviewDecision);
        Assert.Null(rowAfterTry.ReviewReason);
        Assert.NotNull(rowAfterTry.DecidedAt);

        await db.SaveReviewDecisionAsync(Repo, 13, ReviewDecision.Skip, "Too risky right now");
        var rowAfterSkip = await db.GetAsync(Repo, 13);
        Assert.NotNull(rowAfterSkip);
        Assert.Equal(ReviewDecision.Skip, rowAfterSkip!.ReviewDecision);
        Assert.Equal("Too risky right now", rowAfterSkip.ReviewReason);
        Assert.NotNull(rowAfterSkip.DecidedAt);
    }

    [Fact]
    public async Task SaveReviewDecision_None_ClearsDecisionAndReason()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        var issue = new GitHubIssueSummary(13, "Add review UI", "https://example.test/13", "enhancement", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(Repo, issue, hasLinkedPr: false, isAssigned: false);

        await db.SaveReviewDecisionAsync(Repo, 13, ReviewDecision.Skip, "not now");
        await db.SaveReviewDecisionAsync(Repo, 13, ReviewDecision.None, null);

        var row = await db.GetAsync(Repo, 13);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.None, row!.ReviewDecision);
        Assert.Null(row.ReviewReason);
        Assert.Null(row.DecidedAt);
    }

    [Fact]
    public async Task QueryReviewQueue_ReturnsScoredOpenWithoutLinkedPrOrAssigned_OrderedByScore()
    {
        await using var temp = new TempDb();
        await using var db = await temp.OpenAsync();

        await db.UpsertSummaryAsync(Repo, new GitHubIssueSummary(1, "A", "https://example/1", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: false, isAssigned: false);
        await db.UpsertSummaryAsync(Repo, new GitHubIssueSummary(2, "B", "https://example/2", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: true, isAssigned: false);
        await db.UpsertSummaryAsync(Repo, new GitHubIssueSummary(3, "C", "https://example/3", "", DateTimeOffset.UtcNow, IsAssigned: false), hasLinkedPr: false, isAssigned: false);
        await db.UpsertSummaryAsync(Repo, new GitHubIssueSummary(4, "D", "https://example/4", "", DateTimeOffset.UtcNow, IsAssigned: true), hasLinkedPr: false, isAssigned: true);

        await db.SaveScoreAsync(Repo, new IssueScore(1, 4, 4, 4, 4, 4, "A"), "claude");
        await db.SaveScoreAsync(Repo, new IssueScore(3, 2, 2, 2, 2, 2, "C"), "claude");
        await db.SaveScoreAsync(Repo, new IssueScore(4, 5, 5, 5, 5, 5, "D"), "claude");

        var rows = await db.QueryReviewQueueAsync(Repo);

        Assert.Collection(rows,
            r => Assert.Equal(1, r.Number),
            r => Assert.Equal(3, r.Number));
    }
}
