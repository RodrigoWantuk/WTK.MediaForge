By contributing to this project, you agree that your contribution may be distributed under the PolyForm Noncommercial License 1.0.0 and may also be used by the project owner under separate commercial licensing terms.

## Required validation for engine/media changes

Run the full GPU/media gate before opening or merging a PR that touches any of:

- `WTK.MediaForge.Windows/Media`
- `WTK.MediaForge.Graphics.Vulkan`
- `WTK.MediaForge.Graphics.D3D11`
- `WTK.MediaForge.Composition/Runtime`
- `WTK.MediaForge.Composition/Outputs`
- `WTK.MediaForge.Composition/Sources`
- capture, provider lifecycle, render-thread, submission, encode, decode, or GPU export paths

Required commands:

```powershell
dotnet restore
dotnet build WTK.MediaForge.sln -c Debug
dotnet test WTK.MediaForge.sln -c Debug
./scripts/test.ps1 -Tier Fast
./scripts/test.ps1 -Tier Gpu
./scripts/verify-media-transport-rules.ps1
./scripts/verify-license-policy.ps1
```

Do not mark MP4, RTMP, hardware encode, hardware decode, or performance gates as product-ready unless these commands pass and the capability report reflects the real backend state.

