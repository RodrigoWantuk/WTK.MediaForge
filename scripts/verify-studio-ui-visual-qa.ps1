param(
    [string]$OutputDirectory = "test-reports"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$reportDirectory = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

Write-Host ">> dotnet test WTK.MediaForge.Studio.Tests -- Studio visual QA"
dotnet test .\WTK.MediaForge.Studio.Tests\WTK.MediaForge.Studio.Tests.csproj `
    --filter "FullyQualifiedName~StudioVisualQaTests" `
    --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$reportPath = Join-Path $reportDirectory "studio-visual-qa-report.md"
$generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
$content = @"
# Studio UI Visual QA Report

Generated: $generatedAt

Status: Passed

Validated by `WTK.MediaForge.Studio.Tests.StudioVisualQaTests`.

Target viewports:

- 1366x768
- 1600x900
- 1920x1080

Covered contract:

- primary navigation keeps scenes separated from reusable sources and outputs;
- bottom workbench remains limited to `Camadas` and `Saídas da cena`;
- production output cards remain visible;
- properties keep contextual selection;
- fit zoom keeps the canvas centered and fully inside the editor viewport;
- primary UI text does not expose engine/backend/debug language.
"@

Set-Content -Path $reportPath -Value $content -Encoding UTF8
Write-Host "Studio UI visual QA passed. Report: $reportPath"
