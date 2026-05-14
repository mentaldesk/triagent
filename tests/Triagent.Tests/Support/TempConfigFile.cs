namespace Triagent.Tests.Support;

/// <summary>
/// A unique temp config-file path for a single test. Deletes on dispose.
/// Replaces the old <c>TempHomeScope</c> pattern that mutated <c>HOME</c>
/// — tests pass <see cref="Path"/> explicitly to <c>TriagentConfig.Load/Save</c>.
/// </summary>
public sealed class TempConfigFile : IDisposable
{
    public string Path { get; }

    public TempConfigFile()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"triagent-test-config-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(Path)) File.Delete(Path); }
        catch { /* best-effort cleanup */ }
    }
}
