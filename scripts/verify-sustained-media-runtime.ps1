param(
    [double]$DurationMinutes = 30,
    [switch]$ReleaseCandidate,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [int]$FramesPerSecond = 60,
    [int]$MaxMemoryGrowthMb = 512,
    [int]$MaxHandleGrowth = 256
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$arguments = @(
    "--sustained-qualification",
    "--duration-minutes", $DurationMinutes.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "--width", $Width,
    "--height", $Height,
    "--fps", $FramesPerSecond,
    "--max-memory-growth-mb", $MaxMemoryGrowthMb,
    "--max-handle-growth", $MaxHandleGrowth,
    "--out", "test-reports"
)
if ($ReleaseCandidate) {
    $arguments += "--release-candidate"
}

dotnet run --project .\WTK.MediaForge.Tools.MediaProofReport -- @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Sustained media runtime qualification failed with exit code $LASTEXITCODE."
}

Write-Host "Sustained qualification passed. Reports: test-reports/sustained-media-qualification.json, test-reports/sustained-media-qualification.md"
