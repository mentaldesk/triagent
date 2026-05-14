#!/usr/bin/env zsh
set -euo pipefail

repo_root="${0:A:h}"
project_path="$repo_root/src/Triagent/Triagent.csproj"

exec dotnet run --project "$project_path" -- "$@"
