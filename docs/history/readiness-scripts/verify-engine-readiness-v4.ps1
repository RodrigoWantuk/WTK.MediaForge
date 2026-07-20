param(
    [switch]$SkipDotNetTest,
    [switch]$SkipGpu,
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
        throw "Engine readiness v4 check failed: $Name"
    }
}

Invoke-Step "verify-media-transport-rules" { & "$PSScriptRoot/verify-media-transport-rules.ps1" }
Invoke-Step "verify-license-policy" { & "$PSScriptRoot/verify-license-policy.ps1" }

Invoke-Step "product capability truth tests" {
    dotnet test `
        WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj `
        --filter "FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests|FullyQualifiedName~ProductReadinessStatusTests|FullyQualifiedName~CapabilityReportTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "documentation truth tests" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~DocsProductTruthTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

if (-not $SkipDotNetTest) {
    Invoke-Step "dotnet test fast-safe projects" {
        dotnet test WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj --filter "Category!=GPU&Category!=Stress" --verbosity minimal -- RunConfiguration.MaxCpuCount=1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        dotnet test WTK.MediaForge.Diagnostics.Tests\WTK.MediaForge.Diagnostics.Tests.csproj --filter "Category!=GPU&Category!=Stress" --verbosity minimal -- RunConfiguration.MaxCpuCount=1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        dotnet test WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj --filter "Category!=GPU&Category!=Stress" --verbosity minimal -- RunConfiguration.MaxCpuCount=1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        dotnet test WTK.MediaForge.Studio.Tests\WTK.MediaForge.Studio.Tests.csproj --filter "Category!=GPU&Category!=Stress" --verbosity minimal -- RunConfiguration.MaxCpuCount=1
    }
}

Invoke-Step "Fast tier" { & "$PSScriptRoot/test.ps1" -Tier Fast }

if (-not $SkipGpu) {
    Invoke-Step "Gpu tier" { & "$PSScriptRoot/test.ps1" -Tier Gpu }
}

if (-not $SkipPerformance) {
    Invoke-Step "Performance tier" { & "$PSScriptRoot/test.ps1" -Tier Performance }
}

Write-Host "Engine readiness v4 checks passed."
