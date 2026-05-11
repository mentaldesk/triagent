using Triagent.Models;
using Triagent.Scoring;
using Xunit;

namespace Triagent.Tests.Scoring;

public class ScorePromptTests
{
    [Fact]
    public void Includes_each_issue_with_a_marker()
    {
        var prompt = ScorePrompt.Build(
        [
            new IssueForScoring(101, "First", "bug", "body one", []),
            new IssueForScoring(102, "Second", "", "body two", ["a comment"]),
        ]);

        Assert.Contains("--- ISSUE #101 ---", prompt);
        Assert.Contains("--- ISSUE #102 ---", prompt);
        Assert.Contains("TITLE: First", prompt);
        Assert.Contains("TITLE: Second", prompt);
        Assert.Contains("a comment", prompt);
    }

    [Fact]
    public void Truncates_very_long_bodies()
    {
        var hugeBody = new string('x', 5_000);
        var prompt = ScorePrompt.Build(
        [
            new IssueForScoring(1, "t", "", hugeBody, [])
        ]);

        // Body cap is 2000 chars + ellipsis; the prompt definitely should not contain the
        // full 5000 char body verbatim
        Assert.DoesNotContain(new string('x', 4_000), prompt);
        Assert.Contains("…", prompt);
    }

    [Fact]
    public void Caps_number_of_comments()
    {
        var manyComments = Enumerable.Range(1, 50).Select(i => $"comment {i}").ToList();
        var prompt = ScorePrompt.Build(
        [
            new IssueForScoring(1, "t", "", "body", manyComments)
        ]);

        // Cap is 10 — comment 11 onwards should not appear
        Assert.Contains("comment 1", prompt);
        Assert.Contains("comment 10", prompt);
        Assert.DoesNotContain("comment 11", prompt);
    }

    [Fact]
    public void Empty_body_renders_as_empty_placeholder()
    {
        var prompt = ScorePrompt.Build(
        [
            new IssueForScoring(1, "t", "", "", [])
        ]);

        Assert.Contains("(empty)", prompt);
    }

    [Fact]
    public void Includes_calibration_examples_for_all_three_bands()
    {
        var prompt = ScorePrompt.Build([new IssueForScoring(999, "t", "", "b", [])]);

        // Reference issues from the initial sentry-dotnet analysis
        Assert.Contains("#5091", prompt);    // 15/15 high
        Assert.Contains("#4733", prompt);    // 15/15 high
        Assert.Contains("#5157", prompt);    // 9/15 medium
        Assert.Contains("#3292", prompt);    // 7/15 medium-low
        Assert.Contains("#757", prompt);     // 5/15 low
        Assert.Contains("#285", prompt);     // 5/15 low

        // Band labels so the model can see the cliff between categories
        Assert.Contains("agent-ready", prompt);
        Assert.Contains("needs clarification", prompt);
        Assert.Contains("human triage required", prompt);
    }

    [Fact]
    public void Calibration_appears_before_the_batch_issues()
    {
        var prompt = ScorePrompt.Build([new IssueForScoring(12345, "real issue title", "", "b", [])]);

        var calibrationIdx = prompt.IndexOf("CALIBRATION EXAMPLES", StringComparison.Ordinal);
        var issuesHeaderIdx = prompt.IndexOf("ISSUES:", StringComparison.Ordinal);
        var batchIdx = prompt.IndexOf("--- ISSUE #12345 ---", StringComparison.Ordinal);

        Assert.True(calibrationIdx > 0, "calibration block should appear in the prompt");
        Assert.True(calibrationIdx < issuesHeaderIdx, "calibration should be before the ISSUES: header");
        Assert.True(issuesHeaderIdx < batchIdx, "ISSUES: header should be before the batch entries");
    }

    [Fact]
    public void Contains_the_rubric_header()
    {
        var prompt = ScorePrompt.Build([new IssueForScoring(1, "t", "", "b", [])]);

        Assert.StartsWith("Score these GitHub issues for AI-agent doability", prompt);
        Assert.Contains("SPECIFICITY", prompt);
        Assert.Contains("CONTAINMENT", prompt);
        Assert.Contains("VERIFIABILITY", prompt);
        Assert.Contains("CLARITY", prompt);
        Assert.Contains("SELF_SUFFICIENCY", prompt);
    }
}
