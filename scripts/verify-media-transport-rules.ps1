# Verifies media transport guard rail tests pass (Fast tier subset).

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "Tests\WTK.MediaForge.Composition.Tests"

Write-Host "Running media transport guard rail tests..."
$testArgs = @(
    "test",
    $testProject,
    "--no-build",
    "--filter",
    "FullyQualifiedName~GuardRails",
    "--verbosity",
    "normal"
)

& dotnet @testArgs
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Error "Media transport guard rail verification failed. See failing test names in the dotnet test output above."
}

Write-Host "Media transport guard rail verification passed."
