param(
    [switch]$RequireHardwareMedia,
    [switch]$SkipProductBoundary
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
        throw "Engine readiness v9 check failed: $Name"
    }
}

Invoke-Step "dotnet build" {
    dotnet build
}

Invoke-Step "Fast tier" {
    & "$PSScriptRoot/test.ps1" -Tier Fast
}

Invoke-Step "media transport guard rails" {
    & "$PSScriptRoot/verify-media-transport-rules.ps1"
}

Invoke-Step "license policy" {
    & "$PSScriptRoot/verify-license-policy.ps1"
}

if (-not $SkipProductBoundary) {
    Invoke-Step "product boundary" {
        & "$PSScriptRoot/verify-product-boundary.ps1"
    }
}

if ($RequireHardwareMedia) {
    Invoke-Step "required hardware media proofs" {
        & "$PSScriptRoot/verify-engine-readiness-v8.ps1" -RequireHardwareMedia
    }
}
else {
    Write-Host "Hardware media proofs were not required. Run with -RequireHardwareMedia to require encoder/decode/output proofs."
}

Write-Host "Engine readiness v9 checks passed."
