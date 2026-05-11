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
