param(
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
        throw "Phase 2 readiness check failed: $Name"
    }
}

Invoke-Step "verify-media-transport-rules" { & "$PSScriptRoot/verify-media-transport-rules.ps1" }
Invoke-Step "verify-license-policy" { & "$PSScriptRoot/verify-license-policy.ps1" }

Invoke-Step "dotnet test" { dotnet test --verbosity minimal }

Invoke-Step "Fast tier" { & "$PSScriptRoot/test.ps1" -Tier Fast }
Invoke-Step "Gpu tier" { & "$PSScriptRoot/test.ps1" -Tier Gpu }

if (-not $SkipPerformance) {
    Invoke-Step "Performance tier" { & "$PSScriptRoot/test.ps1" -Tier Performance }
}

Write-Host "Phase 2 readiness checks passed."
