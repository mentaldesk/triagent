using System.Globalization;
using Microsoft.Data.Sqlite;
using Triagent.Models;

namespace Triagent.Data;

public sealed class IssueDb : IAsyncDisposable
{
    private const string DateFormat = "o";
    private readonly SqliteConnection _connection;

    private IssueDb(SqliteConnection connection) => _connection = connection;

    public static async Task<IssueDb> OpenAsync(string path, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(ct);

        var db = new IssueDb(connection);
        await db.MigrateAsync(ct);
        return db;
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        const string createSql = """
            CREATE TABLE IF NOT EXISTS issues (
              repo                 TEXT    NOT NULL,
              number               INTEGER NOT NULL,
              title                TEXT    NOT NULL,
              url                  TEXT    NOT NULL,
              labels               TEXT    NOT NULL DEFAULT '',
              state                TEXT    NOT NULL,
              has_linked_pr        INTEGER NOT NULL DEFAULT 0,
              has_assignees        INTEGER NOT NULL DEFAULT 0,
              assignees            TEXT    NOT NULL DEFAULT '',
              github_updated_at    TEXT    NOT NULL,
              closed_at            TEXT,
              last_scored_at       TEXT,
              scorer               TEXT,
              specificity          INTEGER,
              containment          INTEGER,
              verifiability        INTEGER,
              clarity              INTEGER,
              self_sufficiency     INTEGER,
              total                INTEGER,
              rationale            TEXT,
              review_decision      TEXT,
              review_reason        TEXT,
              decided_at           TEXT,
              PRIMARY KEY (repo, number)
            );
            """;

        await using (var createCmd = _connection.CreateCommand())
        {
            createCmd.CommandText = createSql;
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        await TryAddColumnAsync("ALTER TABLE issues ADD COLUMN review_decision TEXT", ct);
        await TryAddColumnAsync("ALTER TABLE issues ADD COLUMN review_reason TEXT", ct);
        await TryAddColumnAsync("ALTER TABLE issues ADD COLUMN decided_at TEXT", ct);

        // Backward compatibility for older DBs.
        await EnsureColumnAsync("issues", "has_assignees", "INTEGER NOT NULL DEFAULT 0", ct);
        await EnsureColumnAsync("issues", "assignees", "TEXT NOT NULL DEFAULT ''", ct);

        // Backward compatibility from earlier in this branch where is_assigned existed.
        var hasLegacyIsAssigned = await HasColumnAsync("issues", "is_assigned", ct);
        if (hasLegacyIsAssigned)
        {
            await using var migrateLegacy = _connection.CreateCommand();
            migrateLegacy.CommandText = """
                UPDATE issues
                SET has_assignees = CASE WHEN has_assignees = 1 OR is_assigned = 1 THEN 1 ELSE 0 END
                """;
            await migrateLegacy.ExecuteNonQueryAsync(ct);
        }

        const string createRepoSettingsSql = """
            CREATE TABLE IF NOT EXISTS repo_settings (
              repo                 TEXT    NOT NULL PRIMARY KEY,
              linked_pr_repos      TEXT    NOT NULL DEFAULT ''
            );
            """;

        await using (var createRepoSettingsCmd = _connection.CreateCommand())
        {
            createRepoSettingsCmd.CommandText = createRepoSettingsSql;
            await createRepoSettingsCmd.ExecuteNonQueryAsync(ct);
        }

        const string indexSql = """
            CREATE INDEX IF NOT EXISTS idx_issues_total ON issues(repo, state, total DESC);
            CREATE INDEX IF NOT EXISTS idx_issues_state ON issues(repo, state);
            CREATE INDEX IF NOT EXISTS idx_issues_review ON issues(repo, review_decision, total DESC);
            """;

        await using var indexCmd = _connection.CreateCommand();
        indexCmd.CommandText = indexSql;
        await indexCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task TryAddColumnAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Already exists.
        }
    }

    public async Task<IssueRow?> GetAsync(string repo, int number, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM issues WHERE repo = $repo AND number = $number";
        cmd.Parameters.AddWithValue("$repo", repo);
        cmd.Parameters.AddWithValue("$number", number);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadRow(reader);
    }

    public async Task<Dictionary<int, (string State, DateTimeOffset? LastScoredAt, DateTimeOffset GitHubUpdatedAt)>>
        GetSyncStateAsync(string repo, CancellationToken ct = default)
    {
        var result = new Dictionary<int, (string, DateTimeOffset?, DateTimeOffset)>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT number, state, last_scored_at, github_updated_at
            FROM issues WHERE repo = $repo
            """;
        cmd.Parameters.AddWithValue("$repo", repo);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var number = reader.GetInt32(0);
            var state = reader.GetString(1);
            var lastScored = reader.IsDBNull(2) ? (DateTimeOffset?)null : ParseDate(reader.GetString(2));
            var updatedAt = ParseDate(reader.GetString(3));
            result[number] = (state, lastScored, updatedAt);
        }
        return result;
    }

    public async Task UpsertSummaryAsync(
        string repo,
        GitHubIssueSummary summary,
        bool hasLinkedPr,
        bool? isAssigned = null,
        CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO issues (repo, number, title, url, labels, state, has_linked_pr, has_assignees, assignees, github_updated_at)
            VALUES ($repo, $number, $title, $url, $labels, 'open', $hasLinkedPr, $hasAssignees, $assignees, $updatedAt)
            ON CONFLICT(repo, number) DO UPDATE SET
              title             = excluded.title,
              url               = excluded.url,
              labels            = excluded.labels,
              state             = 'open',
              has_linked_pr     = excluded.has_linked_pr,
              has_assignees     = excluded.has_assignees,
              assignees         = excluded.assignees,
              github_updated_at = excluded.github_updated_at,
              closed_at         = NULL
            """;
        cmd.Parameters.AddWithValue("$repo", repo);
        cmd.Parameters.AddWithValue("$number", summary.Number);
        cmd.Parameters.AddWithValue("$title", summary.Title);
        cmd.Parameters.AddWithValue("$url", summary.Url);
        cmd.Parameters.AddWithValue("$labels", summary.Labels);
        cmd.Parameters.AddWithValue("$hasLinkedPr", hasLinkedPr ? 1 : 0);
        var assigned = isAssigned ?? summary.HasAssignees;
        cmd.Parameters.AddWithValue("$hasAssignees", (summary.HasAssignees || assigned) ? 1 : 0);
        cmd.Parameters.AddWithValue("$assignees", summary.Assignees ?? string.Empty);
        cmd.Parameters.AddWithValue("$updatedAt", summary.UpdatedAt.ToString(DateFormat, CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveScoreAsync(
        string repo,
        IssueScore score,
        string scorer,
        CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE issues SET
              last_scored_at    = $now,
              scorer            = $scorer,
              specificity       = $specificity,
              containment       = $containment,
              verifiability     = $verifiability,
              clarity           = $clarity,
              self_sufficiency  = $selfSufficiency,
              total             = $total,
              rationale         = $rationale
            WHERE repo = $repo AND number = $number
            """;
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$scorer", scorer);
        cmd.Parameters.AddWithValue("$specificity", score.Specificity);
        cmd.Parameters.AddWithValue("$containment", score.Containment);
        cmd.Parameters.AddWithValue("$verifiability", score.Verifiability);
        cmd.Parameters.AddWithValue("$clarity", score.Clarity);
        cmd.Parameters.AddWithValue("$selfSufficiency", score.SelfSufficiency);
        cmd.Parameters.AddWithValue("$total", score.Total);
        cmd.Parameters.AddWithValue("$rationale", score.Rationale);
        cmd.Parameters.AddWithValue("$repo", repo);
        cmd.Parameters.AddWithValue("$number", score.Number);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveReviewDecisionAsync(
        string repo,
        int number,
        ReviewDecision decision,
        string? reason,
        CancellationToken ct = default)
    {
        if (decision == ReviewDecision.Skip && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Skip decision requires a reason.", nameof(reason));

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE issues SET
              review_decision = $decision,
              review_reason   = $reason,
              decided_at      = CASE WHEN $decision IS NULL THEN NULL ELSE $now END
            WHERE repo = $repo AND number = $number
            """;
        cmd.Parameters.AddWithValue("$decision", (object?)decision.ToDbValue() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", (object?)normalizedReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$repo", repo);
        cmd.Parameters.AddWithValue("$number", number);

        var updated = await cmd.ExecuteNonQueryAsync(ct);
        if (updated != 1)
            throw new InvalidOperationException($"No issue row updated for {repo}#{number}.");
    }

    public async Task<IReadOnlyList<IssueRow>> QueryReviewQueueAsync(string? repo, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM issues WHERE state = 'open' AND total IS NOT NULL AND has_linked_pr = 0 AND has_assignees = 0";
        if (repo is not null) sql += " AND repo = $repo";
        sql += " ORDER BY total DESC, number DESC";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (repo is not null) cmd.Parameters.AddWithValue("$repo", repo);

        var rows = new List<IssueRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        return rows;
    }

    public async Task ArchiveClosedAsync(string repo, IReadOnlyCollection<int> closedNumbers, CancellationToken ct = default)
    {
        if (closedNumbers.Count == 0) return;
        await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(ct);
        foreach (var number in closedNumbers)
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE issues
                SET state = 'closed', closed_at = COALESCE(closed_at, $now)
                WHERE repo = $repo AND number = $number AND state = 'open'
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$repo", repo);
            cmd.Parameters.AddWithValue("$number", number);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<IssueRow>> QueryTopAsync(
        string? repo,
        string state,
        int limit,
        int? minTotal = null,
        string? labelContains = null,
        bool excludeAssigned = false,
        CancellationToken ct = default)
    {
        var sql = "SELECT * FROM issues WHERE total IS NOT NULL";
        if (state != "all") sql += " AND state = $state";
        if (repo is not null) sql += " AND repo = $repo";
        if (minTotal is not null) sql += " AND total >= $minTotal";
        if (labelContains is not null) sql += " AND labels LIKE $labelLike";
        if (excludeAssigned) sql += " AND has_assignees = 0";
        sql += " AND has_linked_pr = 0 ORDER BY total DESC, number DESC LIMIT $limit";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (state != "all") cmd.Parameters.AddWithValue("$state", state);
        if (repo is not null) cmd.Parameters.AddWithValue("$repo", repo);
        if (minTotal is not null) cmd.Parameters.AddWithValue("$minTotal", minTotal.Value);
        if (labelContains is not null) cmd.Parameters.AddWithValue("$labelLike", $"%{labelContains}%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var rows = new List<IssueRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        return rows;
    }

    public async Task<IReadOnlyList<IssueRow>> AllAsync(string? repo, bool includeClosed, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM issues WHERE 1 = 1";
        if (!includeClosed) sql += " AND state = 'open'";
        if (repo is not null) sql += " AND repo = $repo";
        sql += " ORDER BY total DESC, number DESC";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (repo is not null) cmd.Parameters.AddWithValue("$repo", repo);

        var rows = new List<IssueRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        return rows;
    }

    public async Task<IReadOnlyList<string>> ListReposAsync(CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT repo, COUNT(*) AS n FROM issues GROUP BY repo ORDER BY repo";
        var repos = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            repos.Add($"{reader.GetString(0)} ({reader.GetInt64(1)} issues)");
        return repos;
    }

    public async Task<IReadOnlyList<string>> GetLinkedPrReposAsync(string repo, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT linked_pr_repos FROM repo_settings WHERE repo = $repo";
        cmd.Parameters.AddWithValue("$repo", repo);

        var raw = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        return raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SetLinkedPrReposAsync(string repo, IReadOnlyCollection<string> linkedPrRepos, CancellationToken ct = default)
    {
        var normalized = linkedPrRepos
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serialized = string.Join('\n', normalized);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO repo_settings (repo, linked_pr_repos)
            VALUES ($repo, $linkedPrRepos)
            ON CONFLICT(repo) DO UPDATE SET linked_pr_repos = excluded.linked_pr_repos
            """;
        cmd.Parameters.AddWithValue("$repo", repo);
        cmd.Parameters.AddWithValue("$linkedPrRepos", serialized);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private static IssueRow ReadRow(SqliteDataReader r)
    {
        var hasAssignees = r.GetInt32(r.GetOrdinal("has_assignees")) != 0;
        return new IssueRow
        {
            Repo = r.GetString(r.GetOrdinal("repo")),
            Number = r.GetInt32(r.GetOrdinal("number")),
            Title = r.GetString(r.GetOrdinal("title")),
            Url = r.GetString(r.GetOrdinal("url")),
            Labels = r.GetString(r.GetOrdinal("labels")),
            State = r.GetString(r.GetOrdinal("state")),
            HasLinkedPr = r.GetInt32(r.GetOrdinal("has_linked_pr")) != 0,
            HasAssignees = hasAssignees,
            Assignees = GetNullableString(r, "assignees") ?? string.Empty,
            GitHubUpdatedAt = ParseDate(r.GetString(r.GetOrdinal("github_updated_at"))),
            ClosedAt = GetNullableDate(r, "closed_at"),
            LastScoredAt = GetNullableDate(r, "last_scored_at"),
            Scorer = GetNullableString(r, "scorer"),
            Specificity = GetNullableInt(r, "specificity"),
            Containment = GetNullableInt(r, "containment"),
            Verifiability = GetNullableInt(r, "verifiability"),
            Clarity = GetNullableInt(r, "clarity"),
            SelfSufficiency = GetNullableInt(r, "self_sufficiency"),
            Total = GetNullableInt(r, "total"),
            Rationale = GetNullableString(r, "rationale"),
            ReviewDecision = ReviewDecisionExtensions.FromDbValue(GetNullableString(r, "review_decision")),
            ReviewReason = GetNullableString(r, "review_reason"),
            DecidedAt = GetNullableDate(r, "decided_at"),
        };
    }

    private static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private async Task<bool> HasColumnAsync(string table, string column, CancellationToken ct)
    {
        await using var check = _connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await check.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task EnsureColumnAsync(string table, string column, string sqlType, CancellationToken ct)
    {
        if (await HasColumnAsync(table, column, ct)) return;

        await using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType}";
        await alter.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset? GetNullableDate(SqliteDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        return r.IsDBNull(ord) ? null : ParseDate(r.GetString(ord));
    }

    private static int? GetNullableInt(SqliteDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    private static string? GetNullableString(SqliteDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
