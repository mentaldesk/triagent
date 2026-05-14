# Triagent

Score GitHub issues for AI-agent doability and keep the results in a local SQLite
database so the analysis stays incremental.

See [DESIGN.md](DESIGN.md) for the full design.

## Status

Early scaffolding. Not yet usable.

## Install

Triagent is packaged as a .NET global tool. Once published, install with:

```
dotnet tool install --global Triagent
```

To build and install from a local clone:

```
dotnet pack src/Triagent/Triagent.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg Triagent
```

Then run `triagent --help`. Ensure `~/.dotnet/tools` is on your `PATH`.

To update or uninstall:

```
dotnet tool update --global Triagent
dotnet tool uninstall --global Triagent
```

## Dev launcher

Use `./dev.cs` from the repo root as a shorthand for:

- `dotnet run --project src/Triagent/Triagent.csproj -- ...`

Examples:

- `./dev.cs analyze getsentry/sentry-dotnet`
- `./dev.cs review`
- `./dev.cs top 20 --state all`

## Linked PR repo scope

Linked PR repo mappings are stored per analyzed target repo in the local DB.

Behavior:

- Primary source: repo-scoped DB settings (`target repo -> linked repos`)
- Fallback/migration: if no repo-scoped mapping exists yet, Triagent uses
  `linkedPrRepos` from `~/.triagent/config.json` once and persists it for that
  target repo.

Manage repo-scoped mappings via CLI:

- `triagent linked-repos list --repo owner/target`
- `triagent linked-repos set --repo owner/target owner/docs owner/tools`
- `triagent linked-repos add --repo owner/target owner/new-repo`
- `triagent linked-repos remove --repo owner/target owner/docs`

`--repo` defaults to `defaultRepo` from config.

This keeps single-repo setups working while preventing cross-repo leakage in
multi-repo usage.

## Sentry observability

Triagent supports Sentry error capture + performance tracing.

Configure DSN via either:

- CLI (highest priority): `--sentry-dsn <dsn>`
- Config file: `~/.triagent/config.json` -> `sentryDsn`

Example:

- `triagent analyze getsentry/sentry-dotnet --sentry-dsn https://...@o0.ingest.sentry.io/0`

When configured, `analyze` emits tracing spans for major phases (GitHub fetch,
DB sync, detail hydration, scoring batches) and captures exceptions with batch
context.

For quick SDK diagnostics (without waiting for analyze), run any command with:

- `./dev.cs stats --sentry-debug`

You can also enable debug logging via env var:

- `SENTRY_DEBUG=1 ./dev.cs stats`

## Top filtering

`triagent top` supports these key filters:

- `--state open|closed|all`
- `--min-score <n>`
- `--label <substring>`
- `--exclude-assigned` to hide issues with one or more current GitHub assignees

By default, assigned issues are still included unless `--exclude-assigned` is passed.
