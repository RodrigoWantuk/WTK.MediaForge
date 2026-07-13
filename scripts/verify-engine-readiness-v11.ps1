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
        throw "Engine readiness v11 check failed: $Name"
    }
}

Invoke-Step "v10 full product boundary" {
    & "$PSScriptRoot/verify-engine-readiness-v10.ps1" `
        -RequireHardwareMedia:$RequireHardwareMedia `
        -SkipPerformance:$SkipPerformance
}

Invoke-Step "hardware media proof set" {
    & "$PSScriptRoot/verify-engine-readiness-v8.ps1" -RequireHardwareMedia:$RequireHardwareMedia
}

Invoke-Step "capability proof aggregation tests" {
    dotnet test .\WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj --no-build --filter "FullyQualifiedName~CapabilityReportTests|FullyQualifiedName~ProductReadinessStatusTests|FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests"
}

Invoke-Step "encoded output route and runtime status tests" {
    dotnet test .\WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj --no-build --filter "FullyQualifiedName~MediaPipelineRuntimeTests|FullyQualifiedName~EncodeSchedulerTargetTests|FullyQualifiedName~RenderedOutputEncodingPipelineTests"
}

Invoke-Step "Windows media proof truth tests" {
    dotnet test .\WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj --no-build --filter "FullyQualifiedName~WindowsMediaCapabilityTruthTests|FullyQualifiedName~HardwareEncodeFoundationTests"
}

Write-Host "Engine readiness v11 checks passed."
