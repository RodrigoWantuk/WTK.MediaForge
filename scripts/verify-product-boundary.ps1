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
        throw "Product boundary verification failed: $Name"
    }
}

Invoke-Step "capability and hardware proof truth" {
    dotnet test `
        WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj `
        --no-build `
        --filter "FullyQualifiedName~CapabilityReportTests|FullyQualifiedName~ProductReadinessStatusTests|FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "encoded output product boundary" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --no-build `
        --filter "FullyQualifiedName~EncodedOutputPipelineTests|FullyQualifiedName~MediaPipelineRuntimeTests|FullyQualifiedName~RenderedOutputEncodeFrameAdapterTests|FullyQualifiedName~RenderedOutputEncodingPipelineTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "docs and media surface guard rails" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --no-build `
        --filter "FullyQualifiedName~DocsProductTruthTests|FullyQualifiedName~SourceCapabilityReadinessTests|FullyQualifiedName~RenderOutputTypeCatalogTests|FullyQualifiedName~RenderOutputSinkComplianceRegistryTests|FullyQualifiedName~GuardRails" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "Windows media boundary truth" {
    dotnet test `
        WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj `
        --no-build `
        --filter "FullyQualifiedName~WindowsMediaCapabilityTruthTests|FullyQualifiedName~WindowsHardwareDecodeBoundaryTests|FullyQualifiedName~WindowsGpuExportEndToEndProofTests|FullyQualifiedName~HardwareEncodeFoundationTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Write-Host "Product boundary verification passed."
