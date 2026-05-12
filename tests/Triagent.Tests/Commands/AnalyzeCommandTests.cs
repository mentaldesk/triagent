using System.Reflection;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class AnalyzeCommandTests
{
    [Fact]
    public void ResolvePrLinkRepos_WhenConfigEmpty_UsesOnlyTargetRepo()
    {
        var result = InvokeResolvePrLinkRepos("getsentry", "sentry-dotnet", []);

        Assert.Collection(result,
            r =>
            {
                Assert.Equal("getsentry", r.Owner);
                Assert.Equal("sentry-dotnet", r.Repo);
            });
    }

    [Fact]
    public void ResolvePrLinkRepos_AddsTargetRepoWhenMissingAndDeduplicates()
    {
        var result = InvokeResolvePrLinkRepos("getsentry", "sentry-dotnet", [
            "getsentry/sentry-docs",
            "getsentry/sentry-docs",
            "owner/repo"
        ]);

        Assert.Collection(result,
            r => { Assert.Equal(("getsentry", "sentry-docs"), (r.Owner, r.Repo)); },
            r => { Assert.Equal(("owner", "repo"), (r.Owner, r.Repo)); },
            r => { Assert.Equal(("getsentry", "sentry-dotnet"), (r.Owner, r.Repo)); });
    }

    [Fact]
    public void ResolvePrLinkRepos_KeepsTargetRepoOnceWhenConfigured()
    {
        var result = InvokeResolvePrLinkRepos("getsentry", "sentry-dotnet", [
            "getsentry/sentry-dotnet",
            "getsentry/sentry-docs"
        ]);

        Assert.Collection(result,
            r => { Assert.Equal(("getsentry", "sentry-dotnet"), (r.Owner, r.Repo)); },
            r => { Assert.Equal(("getsentry", "sentry-docs"), (r.Owner, r.Repo)); });
    }

    private static List<(string Owner, string Repo)> InvokeResolvePrLinkRepos(
        string owner,
        string repo,
        IReadOnlyList<string> configured)
    {
        var t = typeof(Triagent.Commands.AnalyzeCommand);
        var m = t.GetMethod("ResolvePrLinkRepos", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolvePrLinkRepos not found.");

        var value = m.Invoke(null, [owner, repo, configured]);
        return value as List<(string Owner, string Repo)>
            ?? throw new InvalidOperationException("Unexpected return type.");
    }
}
