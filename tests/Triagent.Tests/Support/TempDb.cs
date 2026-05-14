using Microsoft.Data.Sqlite;
using Triagent.Config;
using Triagent.Data;

namespace Triagent.Tests.Support;

/// <summary>
/// A unique shared-memory SQLite database for a single test. Holds an open keep-alive
/// connection so the data survives across <see cref="OpenAsync"/> calls; the DB is
/// reclaimed when this object is disposed.
///
/// Drop-in replacement for the old `Path.Combine(Path.GetTempPath(), "...db")` pattern.
/// No disk I/O, no leftover files, safe for parallel tests.
/// </summary>
public sealed class TempDb : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }

    public TempDb()
    {
        var name = $"triagent-test-{Guid.NewGuid():N}";
        ConnectionString = $"Data Source=file:{name}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public Task<IssueDb> OpenAsync(CancellationToken ct = default)
        => IssueDb.OpenWithConnectionStringAsync(ConnectionString, ct);

    /// <summary>
    /// Builds a <see cref="TriagentConfig"/> that points at this in-memory DB. The
    /// config's <c>SourcePath</c> is left null (no on-disk config).
    /// </summary>
    public TriagentConfig MakeConfig(string? defaultRepo = null)
        => new()
        {
            DbPath = ConnectionString,
            DefaultRepo = defaultRepo,
        };

    public ValueTask DisposeAsync()
    {
        _keepAlive.Dispose();
        return ValueTask.CompletedTask;
    }
}
