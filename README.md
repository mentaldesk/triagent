# Triagent

Score GitHub issues for AI-agent doability and keep the results in a local SQLite
database so the analysis stays incremental.

See [DESIGN.md](DESIGN.md) for the full design.

## Status

Early scaffolding. Not yet usable.

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

## Top filtering

`triagent top` supports these key filters:

- `--state open|closed|all`
- `--min-score <n>`
- `--label <substring>`
- `--exclude-assigned` to hide issues with one or more current GitHub assignees

By default, assigned issues are still included unless `--exclude-assigned` is passed.
