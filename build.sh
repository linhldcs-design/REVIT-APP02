#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$SCRIPT_DIR/_build/_build.csproj" -- "$@"
