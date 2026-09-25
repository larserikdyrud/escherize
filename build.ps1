<#
.SYNOPSIS
    Builds and tests the Escherize solution.

.DESCRIPTION
    Run this before every commit (SPEC §10). The build must produce zero warnings and
    every test must pass before moving on to the next phase.

.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.

.PARAMETER NoTest
    Build only, skipping the test run.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoTest
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'Escherize.sln'

Write-Host "==> restore" -ForegroundColor Cyan
dotnet restore $solution --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> build ($Configuration)" -ForegroundColor Cyan
dotnet build $solution --configuration $Configuration --no-restore --nologo -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoTest) {
    Write-Host "==> test ($Configuration)" -ForegroundColor Cyan
    dotnet test $solution --configuration $Configuration --no-build --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "==> ok" -ForegroundColor Green
exit 0
