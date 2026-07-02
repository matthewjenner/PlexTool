#!/usr/bin/env bash
#
# Clean, rebuild, and run PlexTool.App.
# Usage:  ./Scripts/run.sh [Debug|Release]
#
set -euo pipefail

CONFIGURATION="${1:-Debug}"
if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
    echo "error: configuration must be 'Debug' or 'Release', got '$CONFIGURATION'" >&2
    exit 1
fi

# Run from the repo root regardless of where the script was invoked.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

step() { printf '\n==> %s\n' "$1"; }

step "Cleaning"
dotnet clean --configuration "$CONFIGURATION"

step "Building"
dotnet build --configuration "$CONFIGURATION"

step "Running PlexTool.App  (Ctrl+C to stop)"
# --no-build: run exactly what was just built, with no implicit rebuild.
dotnet run --project Src/PlexTool.App --configuration "$CONFIGURATION" --no-build
