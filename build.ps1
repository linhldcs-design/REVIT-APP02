#!/usr/bin/env pwsh
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& dotnet run --project "$root\_build\_build.csproj" -- $args
exit $LASTEXITCODE
