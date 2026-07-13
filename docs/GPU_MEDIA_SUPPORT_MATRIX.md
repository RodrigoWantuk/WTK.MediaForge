# GPU Media Support Matrix

Support is determined by **runtime-detected capability**, not marketing GPU names.
Windows media probes intentionally do not advertise H.264 decode/encode codecs
from static GPU names alone. Media Foundation proof runners may validate real
hardware output on the current machine, but prototype bridges are excluded from
product capability reports.

Hardware acceleration is mandatory for continuous video decode and encode.
If a backend cannot keep decompressed frames on GPU/VRAM, the capability is
`Unsupported`, `Planned`, or `Unavailable`; it must not fall back to software
decode/encode or CPU staging. Vendor names such as NVIDIA, AMD/Radeon, Intel,
or Apple describe possible adapters only after runtime probing confirms the
actual OS API, codec, surface type, and validation evidence.

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

## Backend Capability Truth

The public capability report includes backend capability entries for
OS-specific hardware media paths. These entries are runtime facts, not feature
marketing:

- Windows backend work lives in `WTK.MediaForge.Windows` and uses
  D3D11/D3D11VA/Media Foundation first.
- Linux backend work must live in a Linux-specific project and target
  VAAPI/DRM/DMABUF, Vulkan Video, or approved vendor interop.
- macOS backend work must live in a macOS-specific project and target
  VideoToolbox/CVPixelBuffer/IOSurface/Metal.
- A backend that requires CPU staging for continuous decoded/encoded frames
  cannot be reported as `Supported` or `Experimental`.
- `Prototype` and `Skeleton` backend readiness cannot be reported as
  user-available, even if the OS or GPU advertises a compatible codec.

## Source Types

| Source | Transport | Status | Notes |
|--------|-----------|--------|-------|
| Desktop capture | GpuSurface | Experimental | D3D11 shared texture |
| Window capture | GpuSurface | Planned | Requires Windows Graphics Capture provider that publishes D3D11 GPU frame leases |
| Webcam | GpuSurface | Planned | Raw CPU input possible at system boundary only; no product GPU-upload provider yet |
| Static image PNG/JPEG | StaticCpuAsset -> D3D11 shared GpuSurface | Supported on Windows product path | Load-time CPU decode; CPU copy released after GPU upload |
| Static image WebP | N/A | Planned | Blocked until license review |
| Video file MP4 | EncodedPacket -> GpuSurface | PrototypeOnly | Windows SourceReader/D3D11VA backend work has started and accepts only IMFDXGIBuffer GPU samples; decode-to-render product proof is not validated |
| RTSP/IP camera | EncodedPacket -> GpuSurface | Planned | Hardware decode required |
| Animated GIF/APNG/WebP | N/A | Planned | Blocked until GPU-safe strategy |
| Lottie | N/A | Planned | Blocked until GPU-safe rasterization |
| NDI input | N/A | Unsupported | License + GPU path required |

## Output Types

| Output | Transport | Status | Notes |
|--------|-----------|--------|-------|
| Preview panel | GpuSurface | Experimental | No CPU readback |
| CPU readback | DebugOnlyCpuReadback | Debug only | Not product |
| Recording MP4 H.264 | EncodedPacket | PrototypeOnly until composite proofs pass | Windows encoded route factory exists and is capability-gated; recording uses non-dropping backpressure and fails observably if frames would be lost. Product support requires hardware encode, render-to-encode, and MP4 output product proofs. |
| RTMP H.264 | EncodedPacket | PrototypeOnly until composite proofs pass | TCP RTMP handshake/publish and FLV H.264 packetization exist; public sink rejects packets without trusted BackendOutputValidated evidence. Product support requires hardware encode, render-to-encode, and RTMP network output proofs. |
| SRT | N/A | Planned | Blocked by license/transport review |
| NDI output | N/A | Unsupported | |
| Virtual camera | N/A | Unsupported | |

## Encoder Paths

| Encoder | Status | Notes |
|---------|--------|-------|
| Media Foundation hardware MFT H.264 | Proof-runner gated / not product output by itself | Windows session uses typed settings, D3D11 device manager, shared MF runtime, and backend packet validation. Product MP4/RTMP still requires the full output proof chain. |
| NVENC direct | Planned | RequiresLegalReview |
| Intel QSV direct | Planned | RequiresLegalReview |
| AMD AMF direct | Planned | RequiresLegalReview |
| libx264 / software H.264 | Prohibited | |
| FFmpeg (future) | Planned / Not used in first product path | Future LGPL-only with review; never a raw video frame product path |

## v8 Media I/O Proof Set and v9/v10 Readiness Gates

Recording MP4 and RTMP remain **PrototypeOnly** as end-to-end product features
until the v8 hardware media proofs pass and the v9/v10 readiness scripts are
green:

```text
FrameScheduler -> EncodeSchedulerTarget -> GpuFrameExporter -> hardware H.264 -> EncodedPacketMp4Muxer
```

`EncodedPacketMp4Muxer` is the packet-only product boundary for MP4 writing: it
does not accept prototype or contract-only packets, and it requires
trusted `BackendOutputValidated` H.264 packet evidence created by an
implementation backend, not by public packet initializers.
`PrototypeEncodedPacketMp4Muxer` remains internal-test-only. Capability API
exposes `HardwareMediaProof` entries for render-to-encode, hardware encode, MP4
recording, hardware decode, decode-to-render, MP4 output, MP4 input, webcam
input, RTMP network output, and NDI input/output. Passed proofs must identify
the backend and carry the required evidence.

Product availability requires the matching product proof, not only an internal
codec/backend proof:

- MP4 output requires render-to-encode, hardware encode, and
  `proof.media_io.mp4_output.product`.
- MP4 input requires hardware decode, decode-to-render, and
  `proof.media_io.mp4_input.product`.
- Webcam input requires `proof.media_io.webcam_input.product`.
- RTMP output requires hardware encode and
  `proof.media_io.rtmp_output.network`.
- NDI input/output require license approval plus their matching NDI product
  proofs.

`./scripts/verify-engine-readiness-v9.ps1` is the default product-boundary gate.
`./scripts/verify-engine-readiness-v10.ps1` adds GPU and Performance tiers for
full local readiness before promotion. `./scripts/verify-engine-readiness-v11.ps1`
adds encoded route/status/backpressure tests and capability proof aggregation
checks.

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

The Windows D3D11VA file decode session opens Media Foundation SourceReader
with a D3D11 device manager and requests NV12 output. It accepts a frame only
when the sample exposes `IMFDXGIBuffer`; system-memory samples fail as
unavailable instead of being uploaded as a product fallback. Accepted textures
are copied by GPU into D3D11 shared texture leases for renderer import.

## FFmpeg / libav Capability Status

FFmpeg is **not used** in the first MP4/RTMP hardware product path.

The first recording/streaming product path must prove the native GPU media path:

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
