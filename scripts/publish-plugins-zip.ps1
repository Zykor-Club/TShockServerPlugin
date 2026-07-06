#!/usr/bin/env pwsh

param(
    [switch]$NoBuild,
    [string]$BuildType = "Debug"
)

Set-Location $PSScriptRoot/..

if (-not $NoBuild) {
    Remove-Item ./out/$BuildType -Recurse -Force -ErrorAction SilentlyContinue
    dotnet build Plugin.slnx -c $BuildType
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Build completed! Output: out/$BuildType/"
