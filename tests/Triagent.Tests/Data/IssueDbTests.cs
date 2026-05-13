using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Data;

public sealed class IssueDbTests
{
    [Fact]
    public async Task QueryTopAsync_ExcludeAssigned_FiltersAssignedOnlyWhenEnabled()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await IssueDb.OpenAsync(dbPath);
            const string repo = "owner/repo";

            var now = DateTimeOffset.UtcNow;
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(1, "Assigned", "https://x/1", "bug", true, "alice", now), hasLinkedPr: false);
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(2, "Unassigned", "https://x/2", "bug", false, "", now), hasLinkedPr: false);
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(3, "PR linked", "https://x/3", "bug", false, "", now), hasLinkedPr: true);

            await db.SaveScoreAsync(repo, new IssueScore(1, 3, 3, 3, 3, 3, "r"), "test");
            await db.SaveScoreAsync(repo, new IssueScore(2, 3, 3, 3, 3, 3, "r"), "test");
            await db.SaveScoreAsync(repo, new IssueScore(3, 3, 3, 3, 3, 3, "r"), "test");

            var all = await db.QueryTopAsync(repo, state: "open", limit: 10, excludeAssigned: false);
            Assert.Equal(new[] { 2, 1 }, all.Select(r => r.Number).ToArray());

            var unassignedOnly = await db.QueryTopAsync(repo, state: "open", limit: 10, excludeAssigned: true);
            Assert.Equal(new[] { 2 }, unassignedOnly.Select(r => r.Number).ToArray());
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task QueryTopAsync_ExcludeAssigned_CombinesWithStateLabelAndMinScore()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await IssueDb.OpenAsync(dbPath);
            const string repo = "owner/repo";

            var now = DateTimeOffset.UtcNow;
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(10, "Bug High", "https://x/10", "bug", false, "", now), hasLinkedPr: false);
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(11, "Bug Assigned", "https://x/11", "bug", true, "bob", now), hasLinkedPr: false);
            await db.UpsertSummaryAsync(repo, new GitHubIssueSummary(12, "Feature", "https://x/12", "feature", false, "", now), hasLinkedPr: false);

            await db.SaveScoreAsync(repo, new IssueScore(10, 3, 3, 3, 3, 3, "r"), "test");
            await db.SaveScoreAsync(repo, new IssueScore(11, 2, 2, 2, 2, 2, "r"), "test");
            await db.SaveScoreAsync(repo, new IssueScore(12, 3, 3, 3, 3, 3, "r"), "test");

            await db.ArchiveClosedAsync(repo, new[] { 12 });

            var rows = await db.QueryTopAsync(
                repo,
                state: "open",
                limit: 10,
                minTotal: 15,
                labelContains: "bug",
                excludeAssigned: true);

            Assert.Equal(new[] { 10 }, rows.Select(r => r.Number).ToArray());
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
