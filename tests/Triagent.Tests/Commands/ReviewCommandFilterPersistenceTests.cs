using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Triagent.Commands;
using Triagent.Config;
using Triagent.Models;
using Triagent.Tests.Support;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class ReviewCommandFilterPersistenceTests
{
    private const string Repo = "getsentry/sentry-dotnet";

    [Fact]
    public async Task CtrlS_persists_skip_filter_to_config()
    {
        await using var temp = new TempDb();
        using var configFile = new TempConfigFile();

        var config = new TriagentConfig
        {
            DbPath = temp.ConnectionString,
            DefaultRepo = Repo,
            ReviewFilter = "all",
        };
        TriagentConfig.Save(config, configFile.Path);

        await SeedScoredIssueAsync(temp, 1001);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app => DriveKeys(app, cts.Token, "Ctrl+S", "Ctrl+Q"),
            ct: cts.Token);

        var reloaded = TriagentConfig.Load(configFile.Path);
        Assert.Equal("skip", reloaded.ReviewFilter);
    }

    [Fact]
    public async Task Restores_filter_from_config_on_startup_and_keeps_it_until_changed()
    {
        await using var temp = new TempDb();
        using var configFile = new TempConfigFile();

        var config = new TriagentConfig
        {
            DbPath = temp.ConnectionString,
            DefaultRepo = Repo,
            ReviewFilter = "skip",
        };
        TriagentConfig.Save(config, configFile.Path);

        await SeedScoredIssueAsync(temp, 1001);
        await SeedScoredIssueAsync(temp, 1002);

        await using (var db = await temp.OpenAsync())
            await db.SaveReviewDecisionAsync(Repo, 1002, ReviewDecision.Skip, "has reason");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ReviewCommand.RunAsync(
            repo: Repo,
            config,
            onAppReady: app => DriveKeys(app, cts.Token, "Ctrl+Q"),
            ct: cts.Token);

        var reloaded = TriagentConfig.Load(configFile.Path);
        Assert.Equal("skip", reloaded.ReviewFilter);
    }

    private static void DriveKeys(IApplication app, CancellationToken ct, params string[] keys)
    {
        var step = 0;
        app.Iteration += (_, _) =>
        {
            if (ct.IsCancellationRequested) return;
            if (step >= keys.Length) return;
            if (Key.TryParse(keys[step++], out var key))
                app.InjectKey(key);
        };
    }

    private static async Task SeedScoredIssueAsync(TempDb temp, int number)
    {
        await using var db = await temp.OpenAsync();
        var issue = new GitHubIssueSummary(number, $"Issue {number}", $"https://example/{number}", "", DateTimeOffset.UtcNow, IsAssigned: false);
        await db.UpsertSummaryAsync(Repo, issue, hasLinkedPr: false, isAssigned: false);
        await db.SaveScoreAsync(Repo, new IssueScore(number, 3, 3, 3, 3, 3, "ok"), "claude");
    }
}
