param(
    [switch]$RequireHardwareMedia,
    [switch]$SkipPerformance
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host ">> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Engine readiness v10 check failed: $Name"
    }
}

Invoke-Step "v9 baseline and product boundary" {
    & "$PSScriptRoot/verify-engine-readiness-v9.ps1" -RequireHardwareMedia:$RequireHardwareMedia
}

Invoke-Step "GPU tier" {
    & "$PSScriptRoot/test.ps1" -Tier Gpu
}

if (-not $SkipPerformance) {
    Invoke-Step "Performance tier" {
        & "$PSScriptRoot/test.ps1" -Tier Performance
    }
}

Write-Host "Engine readiness v10 checks passed."
