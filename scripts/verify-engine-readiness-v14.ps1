param(
    [switch]$RequireHardwareMedia,
    [switch]$SkipPerformance,
    [switch]$RunLocalQualification,
    [switch]$ReleaseCandidateQualification
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($RunLocalQualification -and $ReleaseCandidateQualification) {
    throw "Choose either -RunLocalQualification or -ReleaseCandidateQualification, not both."
}

$startedAt = [DateTimeOffset]::UtcNow
$steps = [System.Collections.Generic.List[object]]::new()
$mediaProofSummary = $null
$previousHardwareRequirement = $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA
if ($RequireHardwareMedia) {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = "1"
}

function Invoke-ReadinessStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ">> $Name"
    $stepStartedAt = [DateTimeOffset]::UtcNow
    try {
        $global:LASTEXITCODE = 0
        & $Action
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) { $exitCode = 0 }

        $status = if ($exitCode -eq 0) { "Passed" } elseif ($exitCode -eq 2) { "Blocked" } else { "Failed" }
        $steps.Add([pscustomobject]@{
            name = $Name
            status = $status
            exitCode = $exitCode
            durationMs = [int]([DateTimeOffset]::UtcNow - $stepStartedAt).TotalMilliseconds
        })

        if ($exitCode -eq 2) { throw [System.InvalidOperationException]::new("Required hardware media evidence is blocked.") }
        if ($exitCode -ne 0) { throw [System.InvalidOperationException]::new("Command exited with code $exitCode.") }
    }
    catch {
        if ($steps.Count -eq 0 -or $steps[$steps.Count - 1].name -ne $Name) {
            $steps.Add([pscustomobject]@{
                name = $Name
                status = "Failed"
                exitCode = 1
                durationMs = [int]([DateTimeOffset]::UtcNow - $stepStartedAt).TotalMilliseconds
            })
        }

        throw
    }
}

function Read-MediaProofSummary {
    $path = Join-Path $repoRoot "test-reports/media-proof-report.json"
    if (-not (Test-Path $path)) {
        throw "Media proof report was not generated at '$path'."
    }

    $report = Get-Content $path -Raw | ConvertFrom-Json
    if ($report.OverallStatus -eq "Failed") {
        throw "Media proof report contains a failed proof."
    }

    if ($RequireHardwareMedia -and
        ($report.OverallStatus -ne "Passed" -or -not $report.ReleaseGatePassed)) {
        throw "Strict media proof report is not Passed."
    }

    if (($report.OverallStatus -eq "Passed") -ne [bool]$report.ReleaseGatePassed) {
        throw "Media proof report has contradictory OverallStatus and ReleaseGatePassed values."
    }

    return [pscustomobject]@{
        overallStatus = [string]$report.OverallStatus
        releaseGatePassed = [bool]$report.ReleaseGatePassed
        requiredFeatureCount = @($report.Features | Where-Object { $_.RequiredForHardwareRelease }).Count
        blockedRequiredFeatureCount = @($report.Features | Where-Object {
            $_.RequiredForHardwareRelease -and $_.Status -ne "Passed"
        }).Count
    }
}

function Write-ReadinessSummary {
    param([string]$Status)

    $reportDirectory = Join-Path $repoRoot "test-reports"
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $summary = [pscustomobject]@{
        schemaVersion = 1
        gate = "v14"
        status = $Status
        requireHardwareMedia = [bool]$RequireHardwareMedia
        qualificationMode = if ($ReleaseCandidateQualification) {
            "ReleaseCandidate8Hours"
        } elseif ($RunLocalQualification) {
            "Local30Minutes"
        } else {
            "Smoke1080p60"
        }
        startedAt = $startedAt
        completedAt = [DateTimeOffset]::UtcNow
        steps = $steps
        mediaProof = $mediaProofSummary
        mediaProofReport = "test-reports/media-proof-report.json"
        sustainedQualificationReport = "test-reports/sustained-media-qualification.json"
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $reportDirectory "engine-readiness-v14.json") -Encoding utf8
}

try {
    Invoke-ReadinessStep "locked dependency restore" {
        dotnet restore .\WTK.MediaForge.sln --locked-mode --verbosity minimal
    }

    Invoke-ReadinessStep "build" {
        dotnet build .\WTK.MediaForge.sln --no-restore --verbosity minimal
    }

    Invoke-ReadinessStep "solution tests" {
        dotnet test .\WTK.MediaForge.sln --no-restore --verbosity minimal
    }

    Invoke-ReadinessStep "fast contracts" {
        & "$PSScriptRoot/test.ps1" -Tier Fast -SkipPolicyChecks
    }

    Invoke-ReadinessStep "GPU lifecycle and integration" {
        & "$PSScriptRoot/test.ps1" -Tier Gpu
    }

    Invoke-ReadinessStep "GPU media transport law" {
        & "$PSScriptRoot/verify-media-transport-rules.ps1"
    }

    Invoke-ReadinessStep "media license policy" {
        & "$PSScriptRoot/verify-license-policy.ps1"
    }

    Invoke-ReadinessStep "hardware media composite proofs" {
        $reportArgs = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", "$PSScriptRoot/generate-media-proof-report.ps1",
            "-OutputDirectory", "test-reports",
            "-Format", "both"
        )
        if ($RequireHardwareMedia) { $reportArgs += "-RequireHardwareMedia" }
        & powershell @reportArgs
    }
    $mediaProofSummary = Read-MediaProofSummary

    if (-not $SkipPerformance) {
        Invoke-ReadinessStep "engine performance workloads" {
            & "$PSScriptRoot/test.ps1" -Tier Performance
        }
    }

    Invoke-ReadinessStep "1080p60 sustained-route smoke" {
        & "$PSScriptRoot/verify-sustained-media-runtime.ps1" `
            -DurationMinutes 0.1 `
            -Width 1920 `
            -Height 1080 `
            -FramesPerSecond 60
    }

    if ($RunLocalQualification) {
        Invoke-ReadinessStep "30-minute sustained engine qualification" {
            & "$PSScriptRoot/verify-sustained-media-runtime.ps1"
        }
    }

    if ($ReleaseCandidateQualification) {
        Invoke-ReadinessStep "8-hour release-candidate qualification" {
            & "$PSScriptRoot/verify-sustained-media-runtime.ps1" -ReleaseCandidate
        }
    }

    Write-ReadinessSummary "Passed"
    Write-Host "Engine readiness v14 passed. Reports: test-reports/engine-readiness-v14.json, test-reports/media-proof-report.json, test-reports/media-proof-report.md"
}
catch {
    $blocked = $steps.Count -gt 0 -and $steps[$steps.Count - 1].exitCode -eq 2
    Write-ReadinessSummary $(if ($blocked) { "Blocked" } else { "Failed" })
    Write-Error "Engine readiness v14 failed: $($_.Exception.Message)"
    if ($blocked) { exit 2 }
    exit 1
}
finally {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = $previousHardwareRequirement
}
