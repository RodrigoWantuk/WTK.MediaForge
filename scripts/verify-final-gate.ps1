param(
    [switch]$RequireHardwareMedia,
    [switch]$RequireRemoteScene,
    [switch]$ReleaseCandidateQualification,
    [string]$DirectRemoteSceneEvidence = "test-reports/remote-scene-direct.json",
    [string]$TurnRemoteSceneEvidence = "test-reports/remote-scene-turn.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet clean .\WTK.MediaForge.sln --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Release clean failed." }
    dotnet restore .\WTK.MediaForge.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed." }
    dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
    dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release --filter "Category!=GPU&Category!=Stress&Category!=Performance" --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Portable Release tests failed." }
    .\scripts\test.ps1 -Tier Fast
    if ($LASTEXITCODE -ne 0) { throw "Fast gate failed." }
    .\scripts\verify-media-transport-rules.ps1
    if ($LASTEXITCODE -ne 0) { throw "GPU media transport policy failed." }
    .\scripts\verify-license-policy.ps1
    if ($LASTEXITCODE -ne 0) { throw "License policy failed." }

    if ($RequireHardwareMedia) {
        $readinessArgs = @("-RequireHardwareMedia")
        if ($ReleaseCandidateQualification) { $readinessArgs += "-ReleaseCandidateQualification" }
        & .\scripts\verify-engine-readiness-v14.ps1 @readinessArgs
        if ($LASTEXITCODE -ne 0) { throw "Windows hardware/RX 580/MP4/RTMP qualification failed." }
    }
    else {
        Write-Warning "Hardware media was not required; this run cannot promote Windows media capabilities."
    }

    if ($RequireRemoteScene) {
        $requiredScenarios = @(
            "DirectConnection", "TurnRelay", "BothPeersBehindCgnat", "PacketLoss",
            "BitrateChange", "KeyFrameRequest", "DisconnectAndReconnect", "AbruptShutdown",
            "SimultaneousMp4", "SimultaneousRtmp", "NestedSceneSource", "ApplyAndLiveEditing")
        foreach ($entry in @(
            @{ Label = "Direct"; Path = $DirectRemoteSceneEvidence; Relay = $false },
            @{ Label = "TURN"; Path = $TurnRemoteSceneEvidence; Relay = $true })) {
            if (-not (Test-Path -LiteralPath $entry.Path)) { throw "$($entry.Label) Remote Scene evidence is missing: $($entry.Path)" }
            $report = Get-Content -LiteralPath $entry.Path -Raw | ConvertFrom-Json
            if ($report.isQualified -ne $true) { throw "$($entry.Label) Remote Scene evidence is not qualified." }
            if ($report.rawCpuVideoPathObserved -ne $false -or $report.deterministicShutdownObserved -ne $true) { throw "$($entry.Label) violates transport/shutdown requirements." }
            if ([double]$report.sustainedDurationSeconds -lt 1800) { throw "$($entry.Label) sustained duration is below 30 minutes." }
            if ([long]$report.reconnectCount -lt 1 -or [long]$report.framesSent -lt 1 -or [long]$report.framesReceived -lt 1 -or [long]$report.keyFrames -lt 1) { throw "$($entry.Label) counters are incomplete." }
            if ($entry.Relay -and [string]::IsNullOrWhiteSpace([string]$report.turnServer)) { throw "TURN server evidence is missing." }
            foreach ($scenario in $requiredScenarios) {
                if ($report.scenarios -notcontains $scenario) { throw "$($entry.Label) scenario is missing: $scenario" }
            }
            if ($report.resources.baselineRestored -ne $true -or [long]$report.resources.finalQueuedPackets -ne 0 -or [long]$report.resources.finalOutstandingLeases -ne 0) { throw "$($entry.Label) resources did not return to baseline." }
        }
    }
    else {
        Write-Warning "Remote Scene was not required; Direct/TURN capabilities remain Unavailable."
    }

    Write-Host "Final gate completed for the explicitly requested evidence scope."
}
finally {
    Pop-Location
}
