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
        throw "Engine readiness v8 check failed: $Name"
    }
}

if ($RequireHardwareMedia) {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = "1"
}
else {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = $null
}

Invoke-Step "v7 baseline readiness gate" {
    & "$PSScriptRoot/verify-engine-readiness-v7.ps1" `
        -SkipDotNetTest:$SkipDotNetTest `
        -SkipGpu:$SkipGpu `
        -SkipPerformance:$SkipPerformance `
        -RequireHardwareMedia:$RequireHardwareMedia
}

Invoke-Step "v8 media I/O capability truth tests" {
    dotnet test `
        Tests\WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj `
        --filter "FullyQualifiedName~CapabilityReportTests|FullyQualifiedName~ProductReadinessStatusTests|FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "v8 source/output product boundary tests" {
    dotnet test `
        Tests\WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~DocsProductTruthTests|FullyQualifiedName~MediaSourceTypeCatalogTests|FullyQualifiedName~SourceCapabilityReadinessTests|FullyQualifiedName~RenderOutputTypeCatalogTests|FullyQualifiedName~RenderOutputSinkComplianceRegistryTests|FullyQualifiedName~EncodedOutputPipelineTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "v8 Windows media I/O boundary tests" {
    dotnet test `
        Tests\WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj `
        --filter "FullyQualifiedName~WindowsMediaCapabilityTruthTests|FullyQualifiedName~WindowsUnavailableLiveSourceProviderFactoryTests|FullyQualifiedName~WindowsVideoFileSourceProviderFactoryTests|FullyQualifiedName~WindowsHardwareDecodeBoundaryTests|FullyQualifiedName~WindowsGpuExportEndToEndProofTests|FullyQualifiedName~HardwareEncodeFoundationTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Write-Host "Engine readiness v8 checks passed."
