# Triagent

Score GitHub issues for AI-agent doability and keep the results in a local SQLite
database so the analysis stays incremental.

See [DESIGN.md](DESIGN.md) for the full design.

## Status

Early scaffolding. Not yet usable.

## Top filtering

`triagent top` supports these key filters:

- `--state open|closed|all`
- `--min-score <n>`
- `--label <substring>`
- `--exclude-assigned` to hide issues with one or more current GitHub assignees

By default, assigned issues are still included unless `--exclude-assigned` is passed.
