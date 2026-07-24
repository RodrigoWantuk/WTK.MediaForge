# Verifies every test class in GPU test assemblies declares Category=GPU at class level.
# Stress tests override Category to Stress at method level — that is expected.

$ErrorActionPreference = "Stop"

$gpuProjects = @(
    "Tests\WTK.MediaForge.Graphics.D3D11.Tests",
    "Tests\WTK.MediaForge.Graphics.Vulkan.Tests",
    "Tests\WTK.MediaForge.Capture.Tests"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$failures = @()

foreach ($project in $gpuProjects) {
    $dir = Join-Path $repoRoot $project
    $files = Get-ChildItem -Path $dir -Filter "*Tests.cs" -Recurse

    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw
        if ($content -notmatch '\[Trait\("Category",\s*TestCategories\.Gpu\)\]') {
            $failures += "$($file.FullName): missing class-level [Trait(`"Category`", TestCategories.Gpu)]"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("GPU trait verification failed:`n" + ($failures -join "`n"))
}

Write-Host "GPU trait verification passed for $($gpuProjects.Count) projects."
