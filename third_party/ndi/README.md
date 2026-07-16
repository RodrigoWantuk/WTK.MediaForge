# NDI Standard SDK Runtime Assets

This directory is intentionally kept source-only by default.

For a licensed/release build that redistributes the Standard NDI SDK runtime,
place the NDI runtime DLLs in:

- `third_party/ndi/windows/x64/Processing.NDI.Lib.x64.dll`
- `third_party/ndi/windows/x86/Processing.NDI.Lib.x86.dll`

`WTK.MediaForge.Windows.csproj` copies these files to the build output and packs
them as NuGet native runtime assets under `runtimes/win-*/native` when they are
present. The files are not downloaded by the build.

Redistribution must follow the NDI SDK license terms, trademark attribution, and
third-party rights shipped with the SDK. MediaForge currently uses the Standard
SDK only for runtime detection and source discovery. Continuous NDI video input
or output remains blocked until a GPU-safe transport path is validated.
