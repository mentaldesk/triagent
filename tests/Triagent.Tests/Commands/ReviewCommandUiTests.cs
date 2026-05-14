using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Triagent.Commands;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class ReviewCommandUiTests
{
    [Fact]
    public async Task CtrlQ_quits_review_ui()
    {
        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet" };

        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 5058, scored: true);

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
            config,
            onAppReady: app =>
            {
                app.Iteration += (_, _) =>
                {
                    if (Key.TryParse("Ctrl+Q", out var quit))
                        app.InjectKey(quit);
                };
            },
            ct: CancellationToken.None);

        Assert.True(true); // completed without hanging
    }

    [Fact]
    public async Task T_marks_selected_issue_as_try()
    {
        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet" };

        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 5058, scored: true);

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
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

        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var row = await db.GetAsync("getsentry/sentry-dotnet", 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.Try, row!.ReviewDecision);
    }

    [Fact]
    public async Task R_reverts_selected_issue_to_unreviewed()
    {
        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet" };

        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 5058, scored: true);
        await using (var db = await IssueDb.OpenAsync(config.ResolvedDbPath))
            await db.SaveReviewDecisionAsync("getsentry/sentry-dotnet", 5058, ReviewDecision.Skip, "temp");

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
            config,
            onAppReady: app =>
            {
                var step = 0;
                app.Iteration += (_, _) =>
                {
                    switch (step++)
                    {
                        case 0:
                            if (Key.TryParse("R", out var r)) app.InjectKey(r);
                            break;
                        case 1:
                            if (Key.TryParse("Ctrl+Q", out var quit)) app.InjectKey(quit);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        await using var db2 = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var row = await db2.GetAsync("getsentry/sentry-dotnet", 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.None, row!.ReviewDecision);
        Assert.Null(row.ReviewReason);
    }


    [Fact]
    public async Task S_then_enter_confirms_skip_when_reason_already_present()
    {
        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet" };

        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 5058, scored: true);
        await using (var db = await IssueDb.OpenAsync(config.ResolvedDbPath))
            await db.SaveReviewDecisionAsync("getsentry/sentry-dotnet", 5058, ReviewDecision.Skip, "already has reason");

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
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

        await using var db2 = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var row = await db2.GetAsync("getsentry/sentry-dotnet", 5058);
        Assert.NotNull(row);
        Assert.Equal(ReviewDecision.Skip, row!.ReviewDecision);
        Assert.Equal("already has reason", row.ReviewReason);
    }

    [Fact]
    public async Task CtrlU_and_CtrlS_filters_do_not_mutate_decisions()
    {
        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet" };

        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 1001, scored: true);
        await SeedIssueAsync(config, "getsentry/sentry-dotnet", 1002, scored: true);

        await using (var db = await IssueDb.OpenAsync(config.ResolvedDbPath))
            await db.SaveReviewDecisionAsync("getsentry/sentry-dotnet", 1002, ReviewDecision.Skip, "skip me");

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
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

        await using var db2 = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var aRow = await db2.GetAsync("getsentry/sentry-dotnet", 1001);
        var bRow = await db2.GetAsync("getsentry/sentry-dotnet", 1002);

        Assert.NotNull(aRow);
        Assert.NotNull(bRow);
        Assert.Equal(ReviewDecision.None, aRow!.ReviewDecision);
        Assert.Equal(ReviewDecision.Skip, bRow!.ReviewDecision);
        Assert.Equal("skip me", bRow.ReviewReason);
    }

    private static async Task SeedIssueAsync(TriagentConfig config, string repo, int number, bool scored)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var issue = new GitHubIssueSummary(number, $"Issue {number}", $"https://example/{number}", "", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(repo, issue, hasLinkedPr: false, isAssigned: false);
        if (scored)
            await db.SaveScoreAsync(repo, new IssueScore(number, 3, 3, 3, 3, 3, "ok"), "claude");
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
}
