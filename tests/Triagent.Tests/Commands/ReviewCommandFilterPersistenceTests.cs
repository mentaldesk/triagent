using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Triagent.Commands;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;
using Xunit;

namespace Triagent.Tests.Commands;

[Collection("ConfigIsolation")]
public sealed class ReviewCommandFilterPersistenceTests
{
    [Fact]
    public async Task CtrlS_persists_skip_filter_to_config()
    {
        using var home = new TempHomeScope();

        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet", ReviewFilter = "all" };
        TriagentConfig.Save(config);

        await SeedIssueAsync(config, 1001);

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
                            if (Key.TryParse("Ctrl+S", out var s)) app.InjectKey(s);
                            break;
                        case 1:
                            if (Key.TryParse("Ctrl+Q", out var q)) app.InjectKey(q);
                            break;
                    }
                };
            },
            ct: CancellationToken.None);

        var reloaded = TriagentConfig.Load();
        Assert.Equal("skip", reloaded.ReviewFilter);
    }

    [Fact]
    public async Task Restores_filter_from_config_on_startup_and_keeps_it_until_changed()
    {
        using var home = new TempHomeScope();

        var dbPath = TempDbPath();
        var config = new TriagentConfig { DbPath = dbPath, DefaultRepo = "getsentry/sentry-dotnet", ReviewFilter = "skip" };
        TriagentConfig.Save(config);

        await SeedIssueAsync(config, 1001);
        await SeedIssueAsync(config, 1002);

        await using (var db = await IssueDb.OpenAsync(config.ResolvedDbPath))
            await db.SaveReviewDecisionAsync("getsentry/sentry-dotnet", 1002, ReviewDecision.Skip, "has reason");

        await ReviewCommand.RunAsync(
            repo: "getsentry/sentry-dotnet",
            config,
            onAppReady: app =>
            {
                app.Iteration += (_, _) =>
                {
                    if (Key.TryParse("Ctrl+Q", out var q)) app.InjectKey(q);
                };
            },
            ct: CancellationToken.None);

        var reloaded = TriagentConfig.Load();
        Assert.Equal("skip", reloaded.ReviewFilter);
    }

    private static async Task SeedIssueAsync(TriagentConfig config, int number)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath);
        var issue = new GitHubIssueSummary(number, $"Issue {number}", $"https://example/{number}", "", false, "", DateTimeOffset.UtcNow);
        await db.UpsertSummaryAsync("getsentry/sentry-dotnet", issue, hasLinkedPr: false);
        await db.SaveScoreAsync("getsentry/sentry-dotnet", new IssueScore(number, 3, 3, 3, 3, 3, "ok"), "claude");
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");

    private sealed class TempHomeScope : IDisposable
    {
        private readonly string? _oldHome;
        private readonly string _home;

        public TempHomeScope()
        {
            _oldHome = Environment.GetEnvironmentVariable("HOME");
            _home = Path.Combine(Path.GetTempPath(), $"triagent-home-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_home);
            Environment.SetEnvironmentVariable("HOME", _home);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HOME", _oldHome);
            try { Directory.Delete(_home, recursive: true); } catch { }
        }
    }
}
