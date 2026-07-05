# GPU Media Support Matrix

Support is determined by **runtime-detected capability**, not marketing GPU names.

## Platform Backends

| Platform | Decode GPU | Encode GPU | GPU interop | Priority | Initial status |
|----------|------------|------------|-------------|----------|----------------|
| Windows NVIDIA | D3D11VA/NVDEC via approved APIs | NVENC or MF hardware MFT | D3D11 shared texture -> Vulkan import | High | Experimental |
| Windows Intel | D3D11VA/Quick Sync | QSV or MF hardware MFT | D3D11 texture / DXGI | High | Experimental |
| Windows AMD | D3D11VA/AMF | AMF or MF hardware MFT | D3D11 texture / DXGI | High | Experimental |
| Linux Intel/AMD | VAAPI/DRM | VAAPI H.264 | DMA-BUF/Vulkan import | Medium | Planned |
| Linux NVIDIA | NVDEC | NVENC | CUDA/Vulkan interop | Medium | Planned |
| macOS | VideoToolbox | VideoToolbox | CVPixelBuffer/IOSurface/Metal | Medium | Planned |
| Vulkan Video | Vulkan Video decode/encode | Vulkan-native | Vulkan-native | Experimental | Planned |

## Source Types

| Source | Transport | Status | Notes |
|--------|-----------|--------|-------|
| Desktop capture | GpuSurface | Experimental | D3D11 shared texture |
| Window capture | GpuSurface | Experimental | |
| Webcam | GpuSurface | Experimental | Raw CPU input possible at boundary only |
| Static image PNG/JPEG | StaticCpuAsset -> GpuSurface | Planned | Load-time CPU decode |
| Static image WebP | — | Planned | Blocked until license review |
| Video file MP4 | EncodedPacket -> GpuSurface | Planned | Hardware decode required |
| RTSP/IP camera | EncodedPacket -> GpuSurface | Planned | Hardware decode required |
| Animated GIF/APNG/WebP | — | Planned | Blocked until GPU-safe strategy |
| Lottie | — | Planned | Blocked until GPU-safe rasterization |
| NDI input | — | Unsupported | License + GPU path required |

## Output Types

| Output | Transport | Status | Notes |
|--------|-----------|--------|-------|
| Preview panel | GpuSurface | Experimental | No CPU readback |
| CPU readback | DebugOnlyCpuReadback | Debug only | Not product |
| Recording MP4 H.264 | EncodedPacket | Blocked until export proof + MF MVP | Windows MF hardware MFT |
| RTMP H.264 | EncodedPacket | Planned | After MP4 MVP |
| SRT | — | Planned | Blocked by license/transport review |
| NDI output | — | Unsupported | |
| Virtual camera | — | Unsupported | |

## Encoder Paths

| Encoder | Status | Notes |
|---------|--------|-------|
| Media Foundation hardware MFT H.264 | RequiresLegalReview | Primary Windows MVP path |
| NVENC direct | Planned | RequiresLegalReview |
| Intel QSV direct | Planned | RequiresLegalReview |
| AMD AMF direct | Planned | RequiresLegalReview |
| libx264 / software H.264 | Prohibited | |
| FFmpeg (future) | NotUsedInMvp | Future LGPL-only with review |

## Export Proof Gate

Recording MP4 remains **Blocked** until Commit 06 proves:

```text
Vulkan offscreen -> D3D11/MF-compatible encoder surface (no CPU readback)
```

Capability API exposes `GpuExportProof` status: Passed / Failed / Pending.
