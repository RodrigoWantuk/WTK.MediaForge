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

        if ($exitCode -eq 2) { throw [System.InvalidOperationException]::new("Hardware media readiness is blocked.") }
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

function Write-ReadinessSummary {
    param([string]$Status)

    $reportDirectory = Join-Path $repoRoot "test-reports"
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $summary = [pscustomobject]@{
        schemaVersion = 1
        gate = "v13"
        status = $Status
        requireHardwareMedia = [bool]$RequireHardwareMedia
        startedAt = $startedAt
        completedAt = [DateTimeOffset]::UtcNow
        steps = $steps
        mediaProofReport = "test-reports/media-proof-report.json"
        sustainedQualificationReport = "test-reports/sustained-media-qualification.json"
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $reportDirectory "engine-readiness-v13.json") -Encoding utf8
}

try {
    Invoke-ReadinessStep "build" {
        dotnet build .\WTK.MediaForge.sln --no-restore --verbosity minimal
    }

    Invoke-ReadinessStep "fast unit tests" {
        & "$PSScriptRoot/test.ps1" -Tier Fast -SkipPolicyChecks
    }

    Invoke-ReadinessStep "GPU lifecycle and integration tests" {
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

    if (-not $SkipPerformance) {
        Invoke-ReadinessStep "real composition performance workloads" {
            & "$PSScriptRoot/test.ps1" -Tier Performance
        }
    }

    Invoke-ReadinessStep "short sustained engine media route" {
        & "$PSScriptRoot/verify-sustained-media-runtime.ps1" `
            -DurationMinutes 0.1 `
            -Width 640 `
            -Height 360 `
            -FramesPerSecond 30
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
    Write-Host "Engine readiness v13 passed. Reports: test-reports/engine-readiness-v13.json, test-reports/media-proof-report.json, test-reports/media-proof-report.md"
}
catch {
    $blocked = $steps.Count -gt 0 -and $steps[$steps.Count - 1].exitCode -eq 2
    Write-ReadinessSummary $(if ($blocked) { "Blocked" } else { "Failed" })
    Write-Error "Engine readiness v13 failed: $($_.Exception.Message)"
    if ($blocked) { exit 2 }
    exit 1
}
finally {
    $env:WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA = $previousHardwareRequirement
}
