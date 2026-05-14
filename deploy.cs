#!/usr/bin/env zsh
set -euo pipefail

repo_root="${0:A:h}"
project_path="$repo_root/src/Triagent/Triagent.csproj"
nupkg_dir="$repo_root/nupkg"

rm -rf "$nupkg_dir"
dotnet pack "$project_path" -c Release -o "$nupkg_dir"

if dotnet tool list --global | awk '{print $1}' | grep -qi '^triagent$'; then
  dotnet tool update --global --add-source "$nupkg_dir" --no-cache Triagent
else
  dotnet tool install --global --add-source "$nupkg_dir" --no-cache Triagent
fi
