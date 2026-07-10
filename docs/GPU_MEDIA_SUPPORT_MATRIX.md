# GPU Media Support Matrix

Support is determined by **runtime-detected capability**, not marketing GPU names.
Windows media probes intentionally do not advertise H.264 decode/encode codecs
until real Media Foundation enumeration and backend output validation land.
Prototype bridges are excluded from product capability reports.

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
| Static image PNG/JPEG | StaticCpuAsset -> D3D11 shared GpuSurface | Supported on Windows MVP | Load-time CPU decode; CPU copy released after GPU upload |
| Static image WebP | — | Planned | Blocked until license review |
| Video file MP4 | EncodedPacket -> GpuSurface | PrototypeOnly | Real decode backend and decode-to-render proof are not validated |
| RTSP/IP camera | EncodedPacket -> GpuSurface | Planned | Hardware decode required |
| Animated GIF/APNG/WebP | — | Planned | Blocked until GPU-safe strategy |
| Lottie | — | Planned | Blocked until GPU-safe rasterization |
| NDI input | — | Unsupported | License + GPU path required |

## Output Types

| Output | Transport | Status | Notes |
|--------|-----------|--------|-------|
| Preview panel | GpuSurface | Experimental | No CPU readback |
| CPU readback | DebugOnlyCpuReadback | Debug only | Not product |
| Recording MP4 H.264 | EncodedPacket | PrototypeOnly | Real MF hardware encoder and production MP4 muxing are not complete |
| RTMP H.264 | EncodedPacket | PrototypeOnly | Current transport is in-memory only, not network RTMP |
| SRT | — | Planned | Blocked by license/transport review |
| NDI output | — | Unsupported | |
| Virtual camera | — | Unsupported | |

## Encoder Paths

| Encoder | Status | Notes |
|---------|--------|-------|
| Media Foundation hardware MFT H.264 | PrototypeOnly / RequiresLegalReview | Real hardware MFT enumeration and validated backend output still required |
| NVENC direct | Planned | RequiresLegalReview |
| Intel QSV direct | Planned | RequiresLegalReview |
| AMD AMF direct | Planned | RequiresLegalReview |
| libx264 / software H.264 | Prohibited | |
| FFmpeg (future) | NotUsedInMvp | Future LGPL-only with review |

## Export Proof Gate

Recording MP4 remains **PrototypeOnly** after Phase 2 Commits 15-16:

```text
FrameScheduler -> EncodeSchedulerTarget -> GpuFrameExporter -> MF H.264 -> PrototypeEncodedPacketMp4Muxer
```

`PrototypeEncodedPacketMp4Muxer` buffers packets in memory and writes an
experimental ISO BMFF structure only for internal validation. Capability API
exposes `GpuExportProof` status: Passed / Failed / Pending, but recording
remains unavailable until real hardware encoder output and production muxing are
validated.

## Decode-To-Render Proof Gate

Video file sources remain **PrototypeOnly** until the audit trail proves the
complete GPU path:

```text
encoded file/source packet
  -> hardware decoder BackendOutputValidated
  -> decoded GPU frame adapted to source frame BackendOutputValidated
  -> renderer source submission BackendOutputValidated
```

Prototype decode events, placeholder textures, CPU readback, or staging buffers
must not satisfy this gate.

## FFmpeg / libav Capability Status

FFmpeg is **not used** in the first MP4/RTMP hardware MVP.

The first recording/streaming MVP must prove the native GPU media path:

```text
GPU rendered output
  -> platform hardware encoder
  -> EncodedVideoPacket
  -> native muxer / packetizer / stream transport
```

FFmpeg libraries may be evaluated in a future dedicated phase only for encoded-packet and container-level work. They must never become a product path for raw decompressed video frames in CPU/RAM.

### Capability entries

| Capability | Status | License Status | Product Use | Reason |
|---|---:|---:|---:|---|
| FFmpeg executable process | Prohibited | Prohibited | No | External `ffmpeg.exe` execution is not accepted as a product encode/decode path. |
| FFmpeg rawvideo pipe | Prohibited | Prohibited | No | Moves decompressed frames through CPU/RAM and violates the GPU Media Law. |
| FFmpeg GPL build | Prohibited | Prohibited | No | GPL components are not acceptable as default commercial product dependencies. |
| FFmpeg nonfree build | Prohibited | Prohibited | No | Requires explicit commercial/legal review and is not allowed by default. |
| `libx264` | Prohibited | Prohibited / RequiresCommercialLicense | No | GPL/commercial licensing conflict; not allowed as default encoder. |
| `libx265` | Prohibited | Prohibited / RequiresCommercialLicense | No | GPL/commercial licensing conflict; not allowed as default encoder. |
| FFmpeg software video encode | Prohibited | Prohibited | No | Violates GPU-first hardware encode strategy and must not be used as fallback. |
| FFmpeg software video decode | Prohibited | Prohibited | No | Continuous video sources must decode into GPU surfaces, not raw CPU frames. |
| `libavformat` demux | Planned | RequiresLegalReview | Future | May be evaluated later for reading encoded packets from containers/streams. |
| `libavformat` mux | Planned | RequiresLegalReview | Future | May be evaluated later for writing already encoded packets into containers. |
| `libavcodec` parser / bitstream filter | Planned | RequiresLegalReview | Future | Allowed only for packet/metadata/bitstream operations, not software decode/encode. |
| `libavutil` auxiliary utilities | Planned | RequiresLegalReview | Future | Allowed only if required by approved packet/container-level FFmpeg usage. |

### Allowed future shape

```text
Container / network stream
  -> LGPL-only FFmpeg library demux
  -> EncodedVideoPacket
  -> hardware decoder
  -> GpuVideoFrameLease
  -> GPU composition
```

### Prohibited shape

```text
Container / network stream
  -> FFmpeg software decode
  -> AVFrame / raw YUV / raw RGBA in CPU RAM
  -> upload to GPU
```

### Required conditions for any future FFmpeg/libav usage

Any future FFmpeg library integration must satisfy all of the following:

- LGPL-only build;
- no `--enable-gpl`;
- no `--enable-nonfree`;
- no `libx264`;
- no `libx265`;
- no rawvideo pipe;
- no software video encode in product runtime;
- no software video decode for continuous video sources;
- no decompressed video frame crossing CPU/RAM during normal runtime;
- dynamic linking unless legal review approves otherwise;
- documented build configuration;
- third-party notices and compliance documentation updated;
- capability report must clearly mark each FFmpeg-backed feature as Supported, Experimental, Planned, RequiresLegalReview, Prohibited, or Unsupported.

Until the dedicated **FFmpeg Libraries Integration Review** phase is completed, all FFmpeg/libav features remain `Planned` or `Prohibited`.
