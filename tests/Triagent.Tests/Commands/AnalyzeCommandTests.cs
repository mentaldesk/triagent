using System.Reflection;
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
}
