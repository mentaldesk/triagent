using Triagent.Commands;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class NextIssueCommandTests
{
    private static IssueRow Row(int number) => new()
    {
        Repo = "mentaldesk/triagent",
        Number = number,
        Title = $"Issue {number}",
        Url = $"https://example.test/{number}",
        State = "open",
        GitHubUpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Render_Default_PrintsFirstNumber()
    {
        Assert.Equal("7", NextIssueCommand.Render([Row(7)], all: false, json: false));
    }

    [Fact]
    public void Render_DefaultWithNoIssues_PrintsZero()
    {
        Assert.Equal("0", NextIssueCommand.Render([], all: false, json: false));
    }

    [Fact]
    public void Render_All_PrintsOneNumberPerLine()
    {
        var output = NextIssueCommand.Render([Row(7), Row(3)], all: true, json: false);

        Assert.Equal(["7", "3"], output.Split(Environment.NewLine));
    }

    [Fact]
    public void Render_AllWithNoIssues_PrintsNothing()
    {
        Assert.Equal("", NextIssueCommand.Render([], all: true, json: false));
    }

    [Fact]
    public void Render_Json_PrintsArrayOfRows()
    {
        var output = NextIssueCommand.Render([Row(7), Row(3)], all: true, json: true);

        using var doc = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal([7, 3], doc.RootElement.EnumerateArray().Select(e => e.GetProperty("number").GetInt32()));
        Assert.Equal("https://example.test/7", doc.RootElement[0].GetProperty("url").GetString());
    }
}
