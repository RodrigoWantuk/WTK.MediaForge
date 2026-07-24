# Raw CPU Frame Allowlist

Source of truth for guard rails: `Tests/WTK.MediaForge.Composition.Tests/GuardRails/RawCpuFrameAllowlist.cs`

## Allowed namespace prefixes

- `WTK.MediaForge.*.Tests`
- `WTK.MediaForge.Diagnostics`
- `WTK.MediaForge.Sample`

## Allowed patterns

- Types ending with `Tests`, `TestDoubles`, `ManualScreenshotService`
- Types marked with `[RawCpuVideoFrameException(...)]`

## Forbidden in product namespaces

- `CpuReadbackSink` outside debug/test
- `WriteableBitmap`, `System.Drawing.Bitmap` in source/sink/encoder paths
- `rawvideo`, `libx264`, `FFmpeg` in product media paths
- software decode/encode fallback for continuous video
- CPU staging for continuous decoded or encoder-input frames

## Static image

Static image load is **not** a raw CPU video exception. It uses `MediaTransportKind.StaticCpuAsset`.
