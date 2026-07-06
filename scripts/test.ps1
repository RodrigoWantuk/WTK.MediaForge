param(
    [ValidateSet("Fast", "Gpu", "Stress", "Performance", "Build")]
    [string]$Tier = "Fast"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$fastFilter = "Category!=GPU&Category!=Stress"
$gpuFilter = "Category=GPU&Category!=Stress"
$stressFilter = "Category=Stress"
$performanceFilter = "Category=Performance"

$coreTests = "WTK.MediaForge.Core.Tests\WTK.MediaForge.Core.Tests.csproj"
$diagnosticsTests = "WTK.MediaForge.Diagnostics.Tests\WTK.MediaForge.Diagnostics.Tests.csproj"
$compositionTests = "WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj"
$studioTests = "WTK.MediaForge.Studio.Tests\WTK.MediaForge.Studio.Tests.csproj"
$d3d11Tests = "WTK.MediaForge.Graphics.D3D11.Tests\WTK.MediaForge.Graphics.D3D11.Tests.csproj"
$vulkanTests = "WTK.MediaForge.Graphics.Vulkan.Tests\WTK.MediaForge.Graphics.Vulkan.Tests.csproj"
$windowsTests = "WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj"
$captureTests = "WTK.MediaForge.Capture.Tests\WTK.MediaForge.Capture.Tests.csproj"

function Invoke-TestProject {
    param(
        [string]$Project,
        [string]$Filter,
        [switch]$MaxCpuOne
    )

    $args = @("test", $Project, "--filter", $Filter, "--verbosity", "minimal")
    if ($MaxCpuOne) {
        $args += "--"
        $args += "RunConfiguration.MaxCpuCount=1"
    }

    Write-Host ">> dotnet $($args -join ' ')"
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

switch ($Tier) {
    "Build" {
        dotnet build WTK.MediaForge.sln -v minimal
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    "Fast" {
        # Explicit projects only — GPU assemblies never run in Fast tier.
        Invoke-TestProject -Project $coreTests -Filter $fastFilter
        Invoke-TestProject -Project $diagnosticsTests -Filter $fastFilter
        Invoke-TestProject -Project $compositionTests -Filter $fastFilter
        Invoke-TestProject -Project $studioTests -Filter $fastFilter
        & "$PSScriptRoot/verify-media-transport-rules.ps1"
        & "$PSScriptRoot/verify-license-policy.ps1"
    }
    "Gpu" {
        # Never run GPU projects via solution — sequential, one at a time.
        Invoke-TestProject -Project $d3d11Tests -Filter $gpuFilter -MaxCpuOne
        Invoke-TestProject -Project $vulkanTests -Filter $gpuFilter -MaxCpuOne
        Invoke-TestProject -Project $captureTests -Filter $gpuFilter -MaxCpuOne
        Invoke-TestProject -Project $windowsTests -Filter $gpuFilter -MaxCpuOne
    }
    "Stress" {
        Invoke-TestProject -Project $d3d11Tests -Filter $stressFilter -MaxCpuOne
        Invoke-TestProject -Project $vulkanTests -Filter $stressFilter -MaxCpuOne
        Invoke-TestProject -Project $captureTests -Filter $stressFilter -MaxCpuOne
    }
    "Performance" {
        Invoke-TestProject -Project $diagnosticsTests -Filter $performanceFilter
        Invoke-TestProject -Project $compositionTests -Filter $performanceFilter
    }
}

Write-Host "Tier '$Tier' completed successfully."
