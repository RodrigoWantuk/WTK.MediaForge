# Build, CI, and Release Gate

## Supported build host

The repository targets .NET 8. Portable contracts build on supported .NET hosts,
but the current production media adapters, Studio integration, hardware proofs,
and release qualification are Windows implementations. Linux and macOS are
product goals; their GPU media adapters are not implemented.

Developer validation:

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore
dotnet test .\WTK.MediaForge.sln --no-restore -m:1
.\scripts\test.ps1 -Tier Fast
```

Changes to GPU ownership, capture, D3D11, Vulkan, render submission, providers,
or shutdown also require:

```powershell
.\scripts\test.ps1 -Tier Gpu
```

## CI and release

Hosted CI performs locked restore, Release build, portable tests, Fast, and the
transport/license policies. It cannot promote hardware media. The opt-in
self-hosted `mediaforge-rx580` job runs the Windows hardware readiness gate.

The aggregate release entrypoint is:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia
```

Release-candidate sustained qualification is explicit:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia -ReleaseCandidateQualification
```

Remote Scene promotion additionally requires reviewed physical evidence from
two machines and both routes:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia -RequireRemoteScene `
  -DirectRemoteSceneEvidence .\test-reports\remote-scene-direct.json `
  -TurnRemoteSceneEvidence .\test-reports\remote-scene-turn.json
```

Omitting a `Require*` switch is a developer validation only and cannot promote
the omitted capability. A contract-only native WebRTC build is never a release
artifact.
