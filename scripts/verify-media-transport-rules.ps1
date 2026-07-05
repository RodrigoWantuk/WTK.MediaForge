# Verifies media transport guard rail tests pass (Fast tier subset).

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "WTK.MediaForge.Composition.Tests"

Write-Host "Running media transport guard rail tests..."
dotnet test $testProject --no-build --filter "FullyQualifiedName~GuardRails" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    dotnet test $testProject --filter "FullyQualifiedName~GuardRails"
    Write-Error "Media transport guard rail verification failed."
}

Write-Host "Media transport guard rail verification passed."
