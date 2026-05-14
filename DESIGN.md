# Triagent — Design

A .NET 10 CLI that scans the open issues on a GitHub repository, scores each one for
how readily an AI coding agent could implement it, and stores the results in a local
SQLite database for incremental updates and querying.

The name is a portmanteau of *triage* and *agent*: it tells you which issues are
agent-ready, which need human triage first, and which should stay on a human's plate.

## Goals

- **Reduce manual triage** — surface the issues that are well-specified, bounded, and
  verifiable, so an agent can be pointed at them with minimal supervision.
- **Be incremental** — re-score only issues that are new or have changed since the
  last run. A full repo scan should be cheap on subsequent runs.
- **Be pluggable** — work with whichever LLM CLI is installed and authenticated
  (Claude Code or ChatGPT Codex initially).
- **Stay simple** — single binary, single SQLite file, no service to deploy.

## Scoring rubric

Each issue is scored 1–3 on five dimensions (higher = more agent-ready):

| Dimension          | 1                                  | 2                          | 3                                 |
| ------------------ | ---------------------------------- | -------------------------- | --------------------------------- |
| `specificity`      | Vague goal                         | Outcome implied            | Clearly defined outcome           |
| `containment`      | Cross-cutting                      | Moderate scope             | Self-contained                    |
| `verifiability`    | No clear success criteria          | Inferable                  | Explicit criteria or tests exist  |
| `clarity`          | Agent must make product decisions  | Some interpretation needed | Pure implementation               |
| `self_sufficiency` | Needs history/expertise to grok    | Some background helpful    | Fully understandable on its own   |

**Total score** = sum, range **5–15**.

- **13–15** — hand to an agent with confidence
- **9–12** — possible after a clarification pass
- **5–8** — needs human triage before any agent should touch it

## Tech stack

| Concern        | Choice                                                      |
| -------------- | ----------------------------------------------------------- |
| Runtime        | .NET 10 (single-project console app, AOT-friendly)          |
| CLI parsing    | `System.CommandLine`                                        |
| GitHub client  | `Octokit.NET` (handles pagination, auth, rate limits)       |
| LLM scorer     | Subprocess to `claude` or `codex` CLI (no API keys in env)  |
| Storage        | SQLite via `Microsoft.Data.Sqlite`                          |
| Output / TTY   | `Spectre.Console` (tables, progress bars)                   |

### Auth assumptions

- **GitHub** — uses `GITHUB_TOKEN` env var if set, otherwise falls back to
  `gh auth token`. No credentials handled by Triagent itself.
- **LLM CLIs** — assumes the chosen `claude` or `codex` binary is on `PATH` and
  already authenticated by the user.

## Pluggable scorer

A simple interface and two implementations:

```csharp
public interface IScorer
{
    string Name { get; }
    Task<IReadOnlyList<IssueScore>> ScoreBatchAsync(
        IReadOnlyList<IssueForScoring> batch,
        CancellationToken ct);
}
```

- `ClaudeCliScorer` — shells out to `claude -p <prompt> --model sonnet`
- `CodexCliScorer` — shells out to the ChatGPT Codex CLI with an equivalent prompt
- Both share a single `ScorePrompt.cs` containing the rubric prompt template.

The scorer is selected at runtime via `--scorer claude|codex` (default: `claude`).
Adding new scorers later (Gemini CLI, local model wrapper, the Anthropic SDK
directly) is a matter of dropping in another `IScorer` implementation.

## Data model (SQLite)

```sql
CREATE TABLE issues (
  repo                 TEXT NOT NULL,
  number               INTEGER NOT NULL,
  title                TEXT NOT NULL,
  url                  TEXT NOT NULL,
  labels               TEXT NOT NULL DEFAULT '',
  state                TEXT NOT NULL,            -- 'open' | 'closed'
  has_linked_pr        INTEGER NOT NULL DEFAULT 0,
  github_updated_at    TEXT NOT NULL,            -- ISO-8601 UTC
  closed_at            TEXT,                     -- set when archived
  last_scored_at       TEXT,                     -- null until scored
  scorer               TEXT,                     -- 'claude' | 'codex' etc.
  specificity          INTEGER,
  containment          INTEGER,
  verifiability        INTEGER,
  clarity              INTEGER,
  self_sufficiency     INTEGER,
  total                INTEGER,
  rationale            TEXT,
  PRIMARY KEY (repo, number)
);

CREATE INDEX idx_issues_total      ON issues(repo, state, total DESC);
CREATE INDEX idx_issues_state      ON issues(repo, state);
```

Default DB location: `~/.triagent/triagent.db`. Overridable with `--db <path>`.

## Incremental analyze flow

```
analyze --repo <owner/name> [--scorer claude|codex] [--force] [--batch-size 5]
```

1. **List open issues** via the GitHub issues API (paged, 100 per call). Each item
   already carries `updated_at` — no comments are fetched yet.
2. **Detect closures.** Any issue currently `state='open'` in our DB that does *not*
   appear in this listing is set to `state='closed'` with `closed_at = now`. We
   never delete rows — history is preserved for stats and re-analysis.
3. **Detect PR linkage.** For each open issue, check whether a PR references it
   (closes/fixes/resolves keyword in an open PR body, OR a `cross-referenced` event
   from a PR on the issue timeline). Triagent scans only an allowed repo scope:
   - the analyzed repo itself, plus
   - repo-scoped linked repos persisted in local DB (`repo_settings.linked_pr_repos`).

   Backward compatibility: when no repo-scoped mapping exists yet for a target
   repo, Triagent falls back to global `linkedPrRepos` config, then persists that
   value as the repo-scoped mapping for future runs.

   If linked, set `has_linked_pr=1` and skip scoring. Surface count in the run summary.
4. **Decide what to score.** An issue needs scoring when:
   - It is not yet in the DB, OR
   - `github_updated_at > last_scored_at`, OR
   - `--force` was passed.
   PR-linked issues are excluded regardless.
5. **Fetch comments** for the to-be-scored set (parallel, throttled).
6. **Batch and score.** Group into N (default 5) issues per scorer call. Upsert
   results back into the DB.
7. **Print summary.** `X scored, Y skipped (unchanged), Z newly closed, W skipped (PR linked).`

### Why archive instead of delete?

Closed issues stay in the DB so historical scores remain queryable (`stats --include-closed`,
`top --state closed`, etc.), and so re-opened issues do not need re-scoring if their
content hasn't changed.

## CLI surface

```
triagent analyze   <owner/repo> [--scorer claude|codex] [--force] [--batch-size N]
triagent top       [N=10] [--repo R] [--state open|closed|all] [--label L] [--min-score S]
triagent show      <number> [--repo R]
triagent stats     [--repo R] [--include-closed]
triagent export    [--repo R] [--format tsv|csv|json] [--output PATH]
triagent repos     -- list repos tracked in the DB
triagent linked-repos list   [--repo R]
triagent linked-repos set    [--repo R] <owner/repo>...
triagent linked-repos add    [--repo R] <owner/repo>...
triagent linked-repos remove [--repo R] <owner/repo>...
```

Default `--repo` can be set in a config file so day-to-day commands don't need it.

## Configuration

`~/.triagent/config.json` (auto-created on first run):

```json
{
  "defaultRepo": "getsentry/sentry-dotnet",
  "defaultScorer": "claude",
  "defaultModel": "sonnet",
  "batchSize": 5,
  "dbPath": "~/.triagent/triagent.db",
  "linkedPrRepos": [
    "getsentry/sentry-docs"
  ]
}
```

`linkedPrRepos` remains supported as a compatibility fallback/seed value. Effective
linked-repo mapping is stored per analyzed target repo in the local DB.

Example effective mapping:

- `getsentry/sentry-dotnet` -> [`getsentry/sentry-docs`]
- `owner/other-repo` -> []

Any setting can be overridden via CLI flags (except linked repo mapping, which is currently persisted and resolved automatically as described above).

## Project layout

```
src/Triagent/
  Program.cs                       -- composition root, CLI wiring
  Commands/
    AnalyzeCommand.cs
    TopCommand.cs
    ShowCommand.cs
    StatsCommand.cs
    ExportCommand.cs
    ReposCommand.cs
  GitHub/
    GitHubClient.cs                -- thin wrapper over Octokit
    PrLinkageDetector.cs           -- timeline + open-PR body scan
  Scoring/
    IScorer.cs
    ClaudeCliScorer.cs
    CodexCliScorer.cs
    ScorePrompt.cs                 -- shared rubric prompt
    BatchRunner.cs                 -- batching, retries, JSON parsing
  Data/
    IssueDb.cs                     -- SQLite open/migrate/upsert/query
    Migrations/001_init.sql
  Models/
    Issue.cs
    IssueScore.cs
    AnalyzeSummary.cs
  Config/
    TriagentConfig.cs
tests/Triagent.Tests/
  ...
DESIGN.md
README.md
.gitignore
LICENSE                            -- chosen later when made public
Triagent.sln
```

## Out of scope (for now)

- Multi-user / hosted mode
- Web UI / dashboard
- Posting labels or comments back to GitHub (we may add `--apply-labels` later)
- Scoring PRs themselves
- Pricing/cost tracking per analyze run
- Built-in `--watch` mode — use cron for now (tracked in issue #1)
- Content-hash based re-scoring to skip label-only bumps (tracked in issue #2)

## Settled defaults

- **Private repos** — scored without any extra flag. If the user can see them via
  their GitHub token, Triagent treats them like any other repo.
- **Re-score trigger** — GitHub's `issue.updated_at` field. Simple and good enough;
  noisy label edits will trigger a re-score until issue #2 lands.
