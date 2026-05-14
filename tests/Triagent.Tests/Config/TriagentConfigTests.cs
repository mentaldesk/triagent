using Triagent.Config;
using Triagent.Tests.Support;
using Xunit;

namespace Triagent.Tests.Config;

public sealed class TriagentConfigTests
{
    [Fact]
    public void Load_WithExplicitPath_CreatesDefaultsWhenMissing()
    {
        using var configFile = new TempConfigFile();
        Assert.False(File.Exists(configFile.Path));

        var config = TriagentConfig.Load(configFile.Path);

        Assert.True(File.Exists(configFile.Path));
        Assert.Equal(configFile.Path, config.SourcePath);
        Assert.Equal("claude", config.DefaultScorer);
    }

    [Fact]
    public void Save_AndLoad_RoundTripsThroughExplicitPath()
    {
        using var configFile = new TempConfigFile();

        var saved = new TriagentConfig { DefaultRepo = "owner/repo", ReviewFilter = "skip" };
        TriagentConfig.Save(saved, configFile.Path);

        Assert.Equal(configFile.Path, saved.SourcePath);

        var loaded = TriagentConfig.Load(configFile.Path);
        Assert.Equal("owner/repo", loaded.DefaultRepo);
        Assert.Equal("skip", loaded.ReviewFilter);
        Assert.Equal(configFile.Path, loaded.SourcePath);
    }

    [Fact]
    public void InstanceSave_WritesBackToSourcePath()
    {
        using var configFile = new TempConfigFile();

        var config = TriagentConfig.Load(configFile.Path);
        config.DefaultRepo = "owner/changed";
        config.Save();

        var reloaded = TriagentConfig.Load(configFile.Path);
        Assert.Equal("owner/changed", reloaded.DefaultRepo);
    }

    [Fact]
    public void InstanceSave_ThrowsWhenSourcePathUnset()
    {
        var config = new TriagentConfig();
        Assert.Throws<InvalidOperationException>(() => config.Save());
    }
}
