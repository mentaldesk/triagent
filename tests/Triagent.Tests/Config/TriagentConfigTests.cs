using Triagent.Config;
using Xunit;

namespace Triagent.Tests.Config;

[Collection("ConfigIsolation")]
public sealed class TriagentConfigTests
{
    [Fact]
    public void ConfigPath_UsesHomeEnvironmentVariable_WhenSet()
    {
        using var home = new TempHomeScope();

        var expected = Path.Combine(home.Home, ".triagent", "config.json");
        Assert.Equal(expected, TriagentConfig.ConfigPath);
    }

    private sealed class TempHomeScope : IDisposable
    {
        private readonly string? _oldHome;
        public string Home { get; }

        public TempHomeScope()
        {
            _oldHome = Environment.GetEnvironmentVariable("HOME");
            Home = Path.Combine(Path.GetTempPath(), $"triagent-home-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Home);
            Environment.SetEnvironmentVariable("HOME", Home);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HOME", _oldHome);
            try { Directory.Delete(Home, recursive: true); } catch { }
        }
    }
}
