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
        throw "Engine readiness v6 check failed: $Name"
    }
}

if ($RequireHardwareMedia) {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = "1"
}
else {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = $null
}

Invoke-Step "verify-media-transport-rules" { & "$PSScriptRoot/verify-media-transport-rules.ps1" }
Invoke-Step "verify-license-policy" { & "$PSScriptRoot/verify-license-policy.ps1" }

Invoke-Step "hardware capability and proof truth tests" {
    dotnet test `
        WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj `
        --filter "FullyQualifiedName~ProductMediaPathsDoNotUsePrototypeEvidenceTests|FullyQualifiedName~ProductReadinessStatusTests|FullyQualifiedName~CapabilityReportTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "documentation and source guard rails" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~DocsProductTruthTests|FullyQualifiedName~RawCpuFrameGuardRailTests|FullyQualifiedName~NoDecodedCpuFrameGuardRailTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "source lifetime and playback failure tests" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~ImageFileSourceRuntimeTests|FullyQualifiedName~VideoSourceRuntimeTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "rendered output encode bridge tests" {
    dotnet test `
        WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj `
        --filter "FullyQualifiedName~EncodeSchedulerTargetTests|FullyQualifiedName~RenderedOutputEncodeFrameAdapterTests|FullyQualifiedName~EncodedOutputPipelineTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "Windows hardware media proof tests" {
    dotnet test `
        WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj `
        --filter "FullyQualifiedName~HardwareEncodeFoundationTests|FullyQualifiedName~WindowsGpuExportEndToEndProofTests|FullyQualifiedName~WindowsMediaCapabilityTruthTests|FullyQualifiedName~WindowsSystemDrawingFontAtlasRasterizerTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

Invoke-Step "Vulkan text and public API tests" {
    dotnet test `
        WTK.MediaForge.Graphics.Vulkan.Tests\WTK.MediaForge.Graphics.Vulkan.Tests.csproj `
        --filter "FullyQualifiedName~TextRenderingTests|FullyQualifiedName~PublicApiSurfaceTests" `
        --verbosity minimal `
        -- RunConfiguration.MaxCpuCount=1
}

if (-not $SkipDotNetTest) {
    Invoke-Step "dotnet test" { dotnet test WTK.MediaForge.sln --verbosity minimal -- RunConfiguration.MaxCpuCount=1 }
}

Invoke-Step "Fast tier" { & "$PSScriptRoot/test.ps1" -Tier Fast }

if (-not $SkipGpu) {
    Invoke-Step "Gpu tier" { & "$PSScriptRoot/test.ps1" -Tier Gpu }
}

if (-not $SkipPerformance) {
    Invoke-Step "Performance tier" { & "$PSScriptRoot/test.ps1" -Tier Performance }
}

Write-Host "Engine readiness v6 checks passed."
