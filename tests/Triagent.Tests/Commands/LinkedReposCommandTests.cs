using Triagent.Commands;
using Triagent.Data;
using Triagent.Tests.Support;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class LinkedReposCommandTests
{
    [Fact]
    public async Task SetAddRemove_ManageRepoScopedLinkedRepos()
    {
        await using var temp = new TempDb();
        var cfg = temp.MakeConfig(defaultRepo: "owner/target");

        var setExit = await LinkedReposCommand.SetAsync("owner/target", new[] { "owner/docs" }, cfg, CancellationToken.None);
        Assert.Equal(0, setExit);

        var addExit = await LinkedReposCommand.AddAsync("owner/target", new[] { "owner/tools", "owner/docs" }, cfg, CancellationToken.None);
        Assert.Equal(0, addExit);

        var removeExit = await LinkedReposCommand.RemoveAsync("owner/target", new[] { "owner/docs" }, cfg, CancellationToken.None);
        Assert.Equal(0, removeExit);

        await using var db = await IssueDb.OpenAsync(cfg.ResolvedDbPath);
        var linked = await db.GetLinkedPrReposAsync("owner/target");
        Assert.Equal(new[] { "owner/tools" }, linked);
    }

    [Fact]
    public async Task Commands_ReturnError_WhenRepoMissingAndNoDefault()
    {
        await using var temp = new TempDb();
        var cfg = temp.MakeConfig();

        var listExit = await LinkedReposCommand.ListAsync(null, cfg, CancellationToken.None);
        var setExit = await LinkedReposCommand.SetAsync(null, new[] { "owner/docs" }, cfg, CancellationToken.None);

        Assert.Equal(1, listExit);
        Assert.Equal(1, setExit);
    }
}
