param(
    [switch]$SkipDotNetTest,
    [switch]$SkipGpu,
    [switch]$SkipPerformance,
    [switch]$RequireHardwareMedia
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
        throw "Engine readiness v7 check failed: $Name"
    }
}

if ($RequireHardwareMedia) {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = "1"
}
else {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = $null
}

Invoke-Step "v6 baseline readiness gate" {
    & "$PSScriptRoot/verify-engine-readiness-v6.ps1" `
        -SkipDotNetTest:$SkipDotNetTest `
        -SkipGpu:$SkipGpu `
        -SkipPerformance:$SkipPerformance `
        -RequireHardwareMedia:$RequireHardwareMedia
}

Invoke-Step "v7 hardware-first capability truth tests" {
    dotnet test `
        Tests\WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj `
        --filter "FullyQualifiedName~CapabilityReportTests|FullyQualifiedName~EncodedVideoPacketEvidenceTests|FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "v7 product packet sink and mux boundary tests" {
    dotnet test `
        Tests\WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~EncodedOutputPipelineTests|FullyQualifiedName~DocsProductTruthTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "v7 Windows hardware media boundary tests" {
    dotnet test `
        Tests\WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj `
        --filter "FullyQualifiedName~HardwareEncodeFoundationTests|FullyQualifiedName~WindowsGpuExportEndToEndProofTests|FullyQualifiedName~WindowsMediaCapabilityTruthTests|FullyQualifiedName~WindowsHardwareDecodeBoundaryTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Write-Host "Engine readiness v7 checks passed."
