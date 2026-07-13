param(
    [string]$OutputDirectory = "test-reports",
    [ValidateSet("json", "markdown", "both")]
    [string]$Format = "both",
    [ValidateSet("windows")]
    [string]$Platform = "windows",
    [switch]$RequireHardwareMedia
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$toolProject = Join-Path $repoRoot "WTK.MediaForge.Tools.MediaProofReport/WTK.MediaForge.Tools.MediaProofReport.csproj"

$toolArgs = @(
    "run",
    "--project",
    $toolProject,
    "--",
    "--out",
    $OutputDirectory,
    "--format",
    $Format,
    "--platform",
    $Platform
)

if ($RequireHardwareMedia) {
    $toolArgs += "--require-hardware-media"
}

& dotnet @toolArgs
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    exit $exitCode
}
