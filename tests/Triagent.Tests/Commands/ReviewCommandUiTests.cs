using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Triagent.Commands;
using Triagent.Models;
using Triagent.Tests.Support;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class ReviewCommandUiTests
{
    private const string Repo = "getsentry/sentry-dotnet";

    [Fact]
    public async Task CtrlQ_quits_review_ui()
    {
        await using var temp = new TempDb();
        var config = temp.MakeConfig(defaultRepo: Repo);

        await SeedIssueAsync(temp, 5058);

        var iterationCount = 0;
        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app =>
            {
                app.Iteration += (_, _) =>
                {
                    iterationCount++;
                    if (Key.TryParse("Ctrl+Q", out var quit))
                        app.InjectKey(quit);
                };
            },
            ct: CancellationToken.None);

        Assert.True(iterationCount > 0, "UI never received an Iteration event — key was not injected");
    }

    [Fact]
    public async Task T_marks_selected_issue_as_try()
    {
        await using var temp = new TempDb();
        var config = temp.MakeConfig(defaultRepo: Repo);

        await SeedIssueAsync(temp, 5058);

        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app =>
            {
                var step = 0;
                app.Iteration += (_, _) =>
                {
                    switch (step++)
                    {
                        case 0:
                            if (Key.TryParse("T", out var t)) app.InjectKey(t);
                            break;
                        case 1:
                            if (Key.TryParse("Ctrl+Q", out var quit)) app.InjectKey(quit);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        await using var db = await temp.OpenAsync();
        var row = await db.GetAsync(Repo, 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.Try, row!.ReviewDecision);
    }

    [Fact]
    public async Task U_reverts_selected_issue_to_unreviewed()
    {
        await using var temp = new TempDb();
        var config = temp.MakeConfig(defaultRepo: Repo);

        await SeedIssueAsync(temp, 5058);
        await using (var db = await temp.OpenAsync())
            await db.SaveReviewDecisionAsync(Repo, 5058, ReviewDecision.Skip, "temp");

        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app =>
            {
                var step = 0;
                app.Iteration += (_, _) =>
                {
                    switch (step++)
                    {
                        case 0:
                            if (Key.TryParse("U", out var u)) app.InjectKey(u);
                            break;
                        case 1:
                            if (Key.TryParse("Ctrl+Q", out var quit)) app.InjectKey(quit);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        await using var db2 = await temp.OpenAsync();
        var row = await db2.GetAsync(Repo, 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.None, row!.ReviewDecision);
        Assert.Null(row.ReviewReason);
    }

    [Fact]
    public async Task S_then_enter_confirms_skip_when_reason_already_present()
    {
        await using var temp = new TempDb();
        var config = temp.MakeConfig(defaultRepo: Repo);

        await SeedIssueAsync(temp, 5058);
        await using (var db = await temp.OpenAsync())
            await db.SaveReviewDecisionAsync(Repo, 5058, ReviewDecision.Skip, "already has reason");

        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app =>
            {
                var step = 0;
                app.Iteration += (_, _) =>
                {
                    switch (step++)
                    {
                        case 0:
                            if (Key.TryParse("S", out var s)) app.InjectKey(s);
                            break;
                        case 1:
                            app.InjectKey(new Key(KeyCode.Enter));
                            break;
                        case 2:
                            if (Key.TryParse("Ctrl+Q", out var quit)) app.InjectKey(quit);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        await using var db2 = await temp.OpenAsync();
        var row = await db2.GetAsync(Repo, 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.Skip, row!.ReviewDecision);
        Assert.Equal("already has reason", row.ReviewReason);
    }

    [Fact]
    public async Task CtrlU_and_CtrlS_filters_do_not_mutate_decisions()
    {
        await using var temp = new TempDb();
        using var configFile = new TempConfigFile();
        var config = temp.MakeConfig(defaultRepo: Repo);
        Triagent.Config.TriagentConfig.Save(config, configFile.Path);

        await SeedIssueAsync(temp, 1001);
        await SeedIssueAsync(temp, 1002);

        await using (var db = await temp.OpenAsync())
            await db.SaveReviewDecisionAsync(Repo, 1002, ReviewDecision.Skip, "skip me");

        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app =>
            {
                var step = 0;
                app.Iteration += (_, _) =>
                {
                    switch (step++)
                    {
                        case 0:
                            if (Key.TryParse("Ctrl+U", out var u)) app.InjectKey(u);
                            break;
                        case 1:
                            if (Key.TryParse("Ctrl+S", out var s)) app.InjectKey(s);
                            break;
                        case 2:
                            if (Key.TryParse("Ctrl+A", out var a)) app.InjectKey(a);
                            break;
                        case 3:
                            if (Key.TryParse("Ctrl+Q", out var quit)) app.InjectKey(quit);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        await using var db2 = await temp.OpenAsync();
        var aRow = await db2.GetAsync(Repo, 1001);
        var bRow = await db2.GetAsync(Repo, 1002);

        Assert.NotNull(aRow);
        Assert.NotNull(bRow);
        Assert.Equal(ReviewDecision.None, aRow!.ReviewDecision);
        Assert.Equal(ReviewDecision.Skip, bRow!.ReviewDecision);
        Assert.Equal("skip me", bRow.ReviewReason);
    }

    private static async Task SeedIssueAsync(TempDb temp, int number)
    {
        await using var db = await temp.OpenAsync();
        var issue = new GitHubIssueSummary(number, $"Issue {number}", $"https://example/{number}", "", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(Repo, issue, hasLinkedPr: false, isAssigned: false);
        await db.SaveScoreAsync(Repo, new IssueScore(number, 3, 3, 3, 3, 3, "ok"), "claude");
    }
}
