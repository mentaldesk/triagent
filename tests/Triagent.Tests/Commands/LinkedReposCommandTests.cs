using Triagent.Commands;
using Triagent.Config;
using Triagent.Data;
using Xunit;

namespace Triagent.Tests.Commands;

public sealed class LinkedReposCommandTests
{
    [Fact]
    public async Task SetAddRemove_ManageRepoScopedLinkedRepos()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db");
        try
        {
            var cfg = new TriagentConfig { DbPath = dbPath, DefaultRepo = "owner/target" };

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
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Commands_ReturnError_WhenRepoMissingAndNoDefault()
    {
        var cfg = new TriagentConfig { DbPath = Path.Combine(Path.GetTempPath(), $"triagent-tests-{Guid.NewGuid():N}.db") };

        var listExit = await LinkedReposCommand.ListAsync(null, cfg, CancellationToken.None);
        var setExit = await LinkedReposCommand.SetAsync(null, new[] { "owner/docs" }, cfg, CancellationToken.None);

        Assert.Equal(1, listExit);
        Assert.Equal(1, setExit);
    }
}
