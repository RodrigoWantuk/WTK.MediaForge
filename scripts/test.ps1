param(
    [ValidateSet("Fast", "Gpu", "Stress", "Performance", "Build")]
    [string]$Tier = "Fast",
    [switch]$SkipPolicyChecks
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$fastFilter = "Category!=GPU&Category!=Stress&Category!=Performance"
$gpuFilter = "Category=GPU&Category!=Stress&Category!=Performance"
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
$remoteTests = "WTK.MediaForge.Remote.Tests\WTK.MediaForge.Remote.Tests.csproj"

function Invoke-TestProject {
    param(
        [string]$Project,
        [string]$Filter,
        [switch]$MaxCpuOne,
        [switch]$RequireTests
    )

    $args = @("test", $Project, "--filter", $Filter, "--verbosity", "minimal")
    if ($MaxCpuOne) {
        $args += "--"
        $args += "RunConfiguration.MaxCpuCount=1"
    }

    Write-Host ">> dotnet $($args -join ' ')"
    $output = & dotnet @args 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { exit $exitCode }

    if ($RequireTests) {
        $joinedOutput = $output -join [Environment]::NewLine
        if ($joinedOutput -match "No test matches" -or
            $joinedOutput -match "No test is available" -or
            $joinedOutput -match "Nenhum teste corresponde" -or
            $joinedOutput -match "Nenhum teste.*dispon.vel" -or
            $joinedOutput -match "Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+0" -or
            $joinedOutput -match "Aprovado!\s+.*Aprovado:\s+0") {
            throw "Required test filter '$Filter' did not execute any tests for project '$Project'."
        }
    }
}

switch ($Tier) {
    "Build" {
        dotnet build WTK.MediaForge.sln -v minimal
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    "Fast" {
        # All non-GPU/non-performance contracts run here, including platform projects.
        Invoke-TestProject -Project $coreTests -Filter $fastFilter
        Invoke-TestProject -Project $diagnosticsTests -Filter $fastFilter
        Invoke-TestProject -Project $compositionTests -Filter $fastFilter
        Invoke-TestProject -Project $studioTests -Filter $fastFilter
        Invoke-TestProject -Project $d3d11Tests -Filter $fastFilter -MaxCpuOne
        Invoke-TestProject -Project $vulkanTests -Filter $fastFilter -MaxCpuOne
        Invoke-TestProject -Project $captureTests -Filter $fastFilter -MaxCpuOne
        Invoke-TestProject -Project $windowsTests -Filter $fastFilter -MaxCpuOne
        Invoke-TestProject -Project $remoteTests -Filter $fastFilter
        if (-not $SkipPolicyChecks) {
            & "$PSScriptRoot/verify-media-transport-rules.ps1"
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & "$PSScriptRoot/verify-license-policy.ps1"
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    "Gpu" {
        # Never run GPU projects via solution; run them sequentially, one at a time.
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
        Invoke-TestProject -Project $compositionTests -Filter $performanceFilter -RequireTests
        Invoke-TestProject -Project $vulkanTests -Filter $performanceFilter -MaxCpuOne -RequireTests
    }
}

Write-Host "Tier '$Tier' completed successfully."
