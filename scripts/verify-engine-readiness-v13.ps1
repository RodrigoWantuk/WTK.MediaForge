param(
    [switch]$RequireHardwareMedia,
    [switch]$SkipPerformance,
    [switch]$RunLocalQualification,
    [switch]$ReleaseCandidateQualification
)

Write-Warning "Readiness v13 is historical. Forwarding to the current v14 gate."

$arguments = @{}
if ($RequireHardwareMedia) { $arguments.RequireHardwareMedia = $true }
if ($SkipPerformance) { $arguments.SkipPerformance = $true }
if ($RunLocalQualification) { $arguments.RunLocalQualification = $true }
if ($ReleaseCandidateQualification) { $arguments.ReleaseCandidateQualification = $true }

& "$PSScriptRoot/verify-engine-readiness-v14.ps1" @arguments
exit $LASTEXITCODE
