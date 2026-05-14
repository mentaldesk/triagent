#!/usr/bin/env zsh
set -euo pipefail

repo_root="${0:A:h}"
project_path="$repo_root/src/Triagent/Triagent.csproj"
nupkg_dir="$repo_root/nupkg"

rm -rf "$nupkg_dir"
dotnet pack "$project_path" -c Release -o "$nupkg_dir"

# Uninstall first so the install always picks up the freshly-packed bits.
# `dotnet tool update` is a no-op when the package version is unchanged,
# which is the common case during local development.
if dotnet tool list --global | awk '{print $1}' | grep -qi '^triagent$'; then
  dotnet tool uninstall --global Triagent
fi

dotnet tool install --global --add-source "$nupkg_dir" --no-cache Triagent
