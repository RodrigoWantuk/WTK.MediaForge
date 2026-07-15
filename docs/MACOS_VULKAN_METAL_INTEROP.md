# macOS Vulkan / Metal Interop

## Status

Skeleton only (Commit 23). No production capture, encode, or preview path uses this interop yet.

## Goal

Enable GPU-first media on macOS by sharing surfaces between:

- Vulkan offscreen compositor output
- Metal-backed VideoToolbox hardware encoder input
- CVPixelBuffer import for decode/capture when a GPU-native path is unavailable

## Constraints

- No uncompressed continuous video frames in CPU/RAM on the product path
- VideoToolbox and CVPixelBuffer are platform boundaries only; Core defines contracts
- FFmpeg is not used in the first hardware MP4/RTMP product path
- Software decode/encode fallback is prohibited for continuous video

## Planned flow

```text
Vulkan offscreen render target
  → Metal shared texture / CVPixelBuffer (GPU-resident)
  → VideoToolbox hardware encoder (H.264)
  → EncodedVideoPacket
```

## Open questions

- MoltenVK export to Metal texture ownership and lifetime
- CVPixelBuffer pool sizing vs render thread cadence
- Audit hooks mirroring the current GPU media proof contracts (`IMediaTransportAuditSink`)

## References

- `WTK.MediaForge.Mac/Media/MacHardwareMediaCapabilityProbe.cs`
- `WTK.MediaForge.Mac/Media/VideoToolboxEncoderBoundary.cs`
- `docs/MEDIA_LICENSE_POLICY.md`
