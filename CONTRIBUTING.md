By contributing to this project, you agree that your contribution may be distributed under the PolyForm Noncommercial License 1.0.0 and may also be used by the project owner under separate commercial licensing terms.

## Mandatory cross-platform contract

Windows and Linux are mandatory development targets.

Every contribution must:

- design portable behavior independently of the operating system;
- keep native APIs and platform details inside the matching platform project;
- prevent portable projects from referencing `WTK.MediaForge.Windows` or another OS-specific implementation project;
- add unit tests that compile and run on both Windows and Linux for portable behavior;
- add dedicated platform tests when behavior legitimately depends on native APIs;
- add new portable projects and test projects to the Linux lists in `.github/workflows/ci.yml`.

A contribution is not ready to merge until both automatic jobs pass:

- `Windows build and tests`;
- `Linux build and tests`.

Do not make a platform pass by excluding a relevant project, suppressing a test, using an incorrect target framework, or silently skipping required behavior.

## Required validation for engine and media changes

Run the complete relevant gate before merging changes to capture, Vulkan, D3D11, composition runtime, outputs, sources, provider lifecycle, render submission, encode, decode, or GPU export paths.

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release
.\scripts\test.ps1 -Tier Fast
.\scripts\test.ps1 -Tier Gpu
.\scripts\verify-media-transport-rules.ps1
.\scripts\verify-license-policy.ps1
```

On Linux, restore, build, and test the portable project sets maintained in `.github/workflows/ci.yml` with locked dependencies.

Do not mark MP4, RTMP, hardware encode, hardware decode, or performance gates as product-ready unless the required validation passes and the capability report reflects the real backend state.
