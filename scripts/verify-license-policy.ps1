# Verifies MEDIA_LICENSE_POLICY constraints are reflected in capability catalog and docs.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repoRoot "docs\MEDIA_LICENSE_POLICY.md"
$capabilityCatalog = Join-Path $repoRoot "WTK.MediaForge.Core\Media\MediaForgeCapabilityCatalog.cs"

if (-not (Test-Path $policyPath)) {
    Write-Error "Missing MEDIA_LICENSE_POLICY.md"
}

if (-not (Test-Path $capabilityCatalog)) {
    Write-Error "Missing MediaForgeCapabilityCatalog.cs"
}

$policy = Get-Content $policyPath -Raw
$catalog = Get-Content $capabilityCatalog -Raw

$checks = @(
    @{
        Name = "libx264 prohibited"
        Pattern = "Prohibited"
        File = $catalog
        Required = $true
    },
    @{
        Name = "FFmpeg deferred in capability catalog"
        Pattern = "Deferred"
        File = $catalog
        Required = $true
    },
    @{
        Name = "Policy documents FFmpeg deferred product path"
        Pattern = "deferred until the native hardware MP4/RTMP product path is sustained"
        File = $policy
        Required = $true
    },
    @{
        Name = "Policy documents libx264 GPL prohibition"
        Pattern = "libx264"
        File = $policy
        Required = $true
    },
    @{
        Name = "PNG approved in policy"
        Pattern = "PNG"
        File = $policy
        Required = $true
    },
    @{
        Name = "JPEG approved in policy"
        Pattern = "JPEG"
        File = $policy
        Required = $true
    }
)

$failed = @()
foreach ($check in $checks) {
    if ($check.File -notmatch [regex]::Escape($check.Pattern)) {
        $failed += $check.Name
    }
}

if ($failed.Count -gt 0) {
    Write-Error ("License policy verification failed: " + ($failed -join ", "))
}

Write-Host "License policy verification passed."
