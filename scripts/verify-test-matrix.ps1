param(
    [ValidateSet("Windows", "Linux")]
    [Parameter(Mandatory = $true)]
    [string]$Runner,
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,
    [switch]$RequireExecution
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$matrixPath = Join-Path $PSScriptRoot "test-matrix.json"
$matrix = Get-Content $matrixPath -Raw | ConvertFrom-Json
$discovered = Get-ChildItem (Join-Path $repoRoot "Tests") -Recurse -Filter "*.Tests.csproj" |
    ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/') } |
    Sort-Object
$classified = @($matrix.assemblies | ForEach-Object { $_.project }) | Sort-Object

$missingClassification = Compare-Object $discovered $classified -PassThru | Where-Object { $_ -in $discovered }
$staleClassification = Compare-Object $discovered $classified -PassThru | Where-Object { $_ -in $classified }
if ($missingClassification) { throw "Unclassified test projects: $($missingClassification -join ', ')" }
if ($staleClassification) { throw "Test-matrix projects not found on disk: $($staleClassification -join ', ')" }

foreach ($assembly in $matrix.assemblies) {
    if (-not $assembly.platforms -or -not $assembly.categories) {
        throw "Test project '$($assembly.project)' has no platform or category classification."
    }
    if ($assembly.platforms -contains "portable" -and (-not (($assembly.platforms -contains "linux") -and ($assembly.platforms -contains "windows")))) {
        throw "Portable test project '$($assembly.project)' must be assigned to Windows and Linux."
    }
}

$workflow = Get-Content (Join-Path $repoRoot ".github/workflows/ci.yml") -Raw
$linuxStart = $workflow.IndexOf("      - name: Restore Linux portable test set")
$linuxEnd = $workflow.IndexOf("      - name: Publish Linux reports")
if ($linuxStart -lt 0 -or $linuxEnd -le $linuxStart) { throw "Unable to locate Linux portable test set in ci.yml." }
$linuxTests = $workflow.Substring($linuxStart, $linuxEnd - $linuxStart)

foreach ($assembly in $matrix.assemblies) {
    $inLinux = $linuxTests.Contains($assembly.project)
    $requiresLinux = $assembly.platforms -contains "linux"
    if ($requiresLinux -and -not $inLinux) { throw "Portable/Linux test project '$($assembly.project)' is missing from Linux CI." }
    if (-not $requiresLinux -and $inLinux) { throw "Platform-specific project '$($assembly.project)' must not run in Linux CI." }
}

$solution = Get-Content (Join-Path $repoRoot "WTK.MediaForge.sln") -Raw
foreach ($assembly in $matrix.assemblies) {
    if (-not $solution.Contains($assembly.project.Replace('/', '\'))) {
        throw "Test project '$($assembly.project)' is missing from the Windows full solution." }
}

$resultDirectory = Split-Path -Parent $ReportPath
$trxFiles = if (Test-Path $resultDirectory) { Get-ChildItem $resultDirectory -Recurse -Filter "*.trx" } else { @() }
$executionByAssembly = @{}
foreach ($assembly in $matrix.assemblies) {
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($assembly.project)
    $executionByAssembly[$assembly.project] = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0 }
    foreach ($trxFile in $trxFiles) {
        [xml]$trx = Get-Content $trxFile.FullName -Raw
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            if (-not $result.testName.StartsWith("$assemblyName.", [StringComparison]::Ordinal)) { continue }
            $executionByAssembly[$assembly.project].total++
            switch ($result.outcome) {
                "Passed" { $executionByAssembly[$assembly.project].passed++ }
                "Failed" { $executionByAssembly[$assembly.project].failed++ }
                default { $executionByAssembly[$assembly.project].skipped++ }
            }
        }
    }
    $selected = if ($Runner -eq "Linux") { $assembly.platforms -contains "linux" } else { $true }
    if ($RequireExecution -and $selected -and $executionByAssembly[$assembly.project].total -eq 0) {
        throw "Selected test project '$($assembly.project)' has no executed tests for the published filter."
    }
}

$report = [ordered]@{
    runner = $Runner
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    filter = "Category!=GPU&Category!=Stress&Category!=Performance"
    assemblies = @($matrix.assemblies | ForEach-Object {
        [ordered]@{
            project = $_.project
            selectedForRunner = if ($Runner -eq "Linux") { $_.platforms -contains "linux" } else { $true }
            platforms = @($_.platforms)
            categories = @($_.categories)
            execution = $executionByAssembly[$_.project]
        }
    })
}
$directory = Split-Path -Parent $ReportPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$report | ConvertTo-Json -Depth 5 | Set-Content -Path $ReportPath -Encoding utf8
Write-Host "Test matrix verified for $Runner. Report: $ReportPath"
