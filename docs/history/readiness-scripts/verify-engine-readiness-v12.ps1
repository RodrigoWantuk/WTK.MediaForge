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
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 2) {
        exit 2
    }

    if ($exitCode -ne 0) {
        throw "Engine readiness v12 check failed: $Name"
    }
}

Invoke-Step "v11 readiness baseline" {
    & "$PSScriptRoot/verify-engine-readiness-v11.ps1" `
        -RequireHardwareMedia:$RequireHardwareMedia `
        -SkipPerformance:$SkipPerformance
}

Invoke-Step "v12 encoded output profile and encoder ownership tests" {
    dotnet test .\Tests\WTK.MediaForge.Composition.Tests\WTK.MediaForge.Composition.Tests.csproj --no-build --filter "FullyQualifiedName~RenderOutputTypeCatalogTests"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet test .\Tests\WTK.MediaForge.Windows.Tests\WTK.MediaForge.Windows.Tests.csproj --no-build --filter "FullyQualifiedName~HardwareEncodeFoundationTests|FullyQualifiedName~WindowsMediaCapabilityTruthTests"
}

Invoke-Step "v12 proof report refresh" {
    $reportArgs = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "$PSScriptRoot/generate-media-proof-report.ps1",
        "-OutputDirectory",
        "test-reports",
        "-Format",
        "both"
    )

    if ($RequireHardwareMedia) {
        $reportArgs += "-RequireHardwareMedia"
    }

    & powershell @reportArgs
}

Write-Host "Engine readiness v12 checks passed. Reports: test-reports/media-proof-report.json, test-reports/media-proof-report.md"
