# Build, CI, and Release Gate

## Supported build hosts

The repository targets .NET 8. Windows and Linux are mandatory development and CI hosts.

Portable contracts, runtime logic, Vulkan code, remote-scene components, and portable tests must build on both platforms. Native integrations remain isolated by operating system:

- Windows owns Win32, D3D11, DXGI, Media Foundation, and current hardware-media adapters;
- Linux owns Linux-native adapters and must never depend on Windows implementation projects;
- portable projects must use portable target frameworks and platform-neutral contracts.

Linux hardware-media adapters are still being implemented, but this does not reduce the Linux build-and-test requirement for portable code.

## Automatic cross-platform CI

`.github/workflows/ci.yml` runs for every push and for pull requests targeting `master`.

Every commit must pass both jobs:

- `Windows build and tests`: locked restore of the complete solution, Release build, portable tests, Fast gate, media-transport policy, and license policy;
- `Linux build and tests`: locked restore and Release build of the maintained portable project set, followed by the complete portable test set.

The two jobs are independent. A successful Windows job cannot compensate for a Linux failure, and a successful Linux job cannot compensate for a Windows failure.

When adding a portable project, add it to the Linux restore/build list in the workflow. When adding a portable test project, add it to the Linux restore/test list in the same change.

The workflow uses self-hosted runners with these labels:

```text
[self-hosted, Windows, X64]
[self-hosted, Linux, X64]
```

A manual dispatch runs the same Windows and Linux baseline. It may additionally enable the strict RX 580 hardware-media qualification job.

## Developer validation

Windows full-solution validation:

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release `
  --filter "Category!=GPU&Category!=Stress&Category!=Performance"
.\scripts\test.ps1 -Tier Fast
```

On Linux, use the portable project and test lists maintained in `.github/workflows/ci.yml`, always with locked restore. Those lists are part of the architecture contract and must remain current.

Changes to GPU ownership, capture, D3D11, Vulkan, render submission, providers, or shutdown also require the hardware-appropriate GPU gate:

```powershell
.\scripts\test.ps1 -Tier Gpu
```

## Release qualification

The automatic Windows/Linux pipeline is the mandatory baseline. It does not by itself promote hardware-media features.

The aggregate release entrypoint is:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia
```

Release-candidate sustained qualification is explicit:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia -ReleaseCandidateQualification
```

Remote Scene promotion additionally requires reviewed physical evidence from two machines and both routes:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia -RequireRemoteScene `
  -DirectRemoteSceneEvidence .\test-reports\remote-scene-direct.json `
  -TurnRemoteSceneEvidence .\test-reports\remote-scene-turn.json
```

Omitting a `Require*` switch is developer validation only and cannot promote the omitted capability. A contract-only native WebRTC build is never a release artifact.
