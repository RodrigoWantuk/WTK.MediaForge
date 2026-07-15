# Current Product Roadmap

This document is the active product roadmap for WTK MediaForge. Historical
commit gates and acceptance notes remain useful evidence, but they are not the
source of truth when they conflict with this roadmap.

## Product Principles

- Continuous video decode and encode are hardware-first. There is no software
  decode/encode fallback for product media paths.
- Do not use software decode/encode fallback for continuous video on any
  platform.
- Decompressed continuous video frames stay on GPU/VRAM. CPU/RAM transport is
  limited to encoded packets, metadata, static image load-time decode, tests,
  debug readback, and documented OS boundary exceptions such as webcam upload.
- Core owns contracts. Windows, Linux, and macOS media adapters live in
  platform-specific projects.
- Capabilities are runtime truth. `Supported` or `Experimental` means the
  capability has the required implementation and proof chain for the current
  scope. Missing hardware, driver/API support, or proof evidence is reported as
  `Unavailable` with a reason.
- Sinks never trigger rendering. Render outputs produce completed GPU surfaces;
  encoded routes consume those surfaces, hardware-encode once per compatible
  output profile, and fan out validated packets.
- Proof runners are not product features by themselves. A feature is product
  available only when the runtime route is implemented and its composite proof
  chain passes on the target hardware.

## Current Product Reality

Product-validated foundations:

- GPU lifecycle, Vulkan submission lifetime, descriptor/framebuffer lifetime,
  provider lifecycle, and engine transactional shutdown.
- CP2 multi-layer Vulkan composition and CP3 solid layer, nested canvas,
  chroma key, color correction, blur, text atlas rendering, transforms,
  crop/rotation/pivot, and cut/fade output transitions for the validated scope.
- Static PNG/JPEG image source on Windows: load-time CPU decode into GPU shared
  texture lease, not continuous raw CPU video.
- Offscreen render output and `PreviewPanelSink` GPU surface presentation
  without CPU readback.
- Encoded packet sinks for MP4 and RTMP. They accept only trusted
  `BackendOutputValidated` H.264 packets in product mode.
- Windows render-to-H.264 proof path on validated AMD/Radeon hardware:
  Vulkan offscreen render -> D3D11 export/conversion -> Media Foundation H.264
  hardware packet.
- Windows MP4 output and RTMP network proof paths using real hardware-validated
  packets and native packet/container boundaries on validated hardware.

Implemented but still product-limited:

- Windows Media Foundation D3D11VA MP4 decode path. The product session rejects
  system-memory samples and requires `IMFDXGIBuffer` GPU textures, but MP4 input
  is promoted only after hardware decode, decode-to-render, and video source
  lifecycle proofs pass together.
- Video file source runtime/provider. The provider uses the real decoder by
  default; if hardware decode is unavailable it fails observably instead of
  using placeholder decode.
- Logical RenderGraph. It carries source-frame resources and skip reasons, but
  the physical GPU pass executor is still the active renderer/snapshot path.
- Performance and recovery suites. Contracts exist, but product performance
  must be measured with sustained render, encode, decode, MP4, and RTMP
  workloads.

Unavailable/planned:

- Webcam product source, window capture product source, NDI input/output, SRT,
  virtual camera, audio capture/mix/mux, Linux VAAPI/DRM backend, Linux Vulkan
  Video backend, and macOS VideoToolbox backend.
- FFmpeg/libav integration. It is deferred until native hardware media routes
  are sustained and legal review approves encoded-packet/container-only usage.

Acceptance evidence for historical milestones remains in:

- `docs/CP2_ACCEPTANCE.md`
- `docs/CP3_SOLID_ACCEPTANCE.md`
- `docs/CP3_NESTED_ACCEPTANCE.md`
- `docs/CP3_CHROMA_ACCEPTANCE.md`
- `docs/PREVIEW_PANEL_ACCEPTANCE.md`

## Active Product Tracks

Execute product work in this order. Each track must leave capability reports and
docs truthful before it is considered complete.

| # | Track | Product acceptance |
|---|---|---|
| 01 | Product roadmap and capability language | No public capability uses historical proof-gate language; missing proof is `Unavailable`, not `PrototypeOnly`. |
| 02 | Sustained render-to-encode | Multiple rendered frames produce backend-validated H.264 packets with GPU-only export/conversion and bounded lifetime. |
| 03 | Encoder hardening | MF session owns device/runtime lifetime, limits pending input surfaces, drains/flushes safely, and reports driver/backend failures. |
| 04 | Encoder input conversion | D3D11 VideoProcessor conversion uses output profile parameters, avoids CPU staging, and reports unsupported color/format paths. |
| 05 | MP4 recording product route | Engine route writes sustained packet-only MP4 with valid ftyp/moov/mdat/avcC, duration, samples, and abort/finalize diagnostics. |
| 06 | RTMP product route | Engine route publishes sustained H.264 FLV tags to a real TCP RTMP endpoint with explicit backpressure and reconnect/failure diagnostics. |
| 07 | MP4 video input | Product provider opens real MP4 files, decodes with hardware D3D11VA into GPU textures, supports play/pause/seek/loop/EOF, and feeds renderer. |
| 08 | Decode-to-render sustained | Generated product MP4 asset decodes to GPU leases and renders through Vulkan without CPU frame transport over sustained playback. |
| 09 | Desktop/window capture | Desktop duplication and Windows Graphics Capture publish GPU leases, survive device/display reset, and share sources safely. |
| 10 | Webcam source | Media Foundation capture enumerates devices, uses `KeepLatest`, immediately uploads any OS raw boundary to GPU, and recovers from device loss. |
| 11 | SceneRuntime and physical RenderGraph | Source acquisition, canvas/effect/output passes, encoded routes, and resource lifetime are compiled into executable GPU pass plans. |
| 12 | GPU resource pooling | Render/effect/export intermediates are pooled, bounded, and retired after GPU fences without leaks or stale handles. |
| 13 | Effects/text through graph | Layer effects, scene effects, text atlas reuse, and route transitions become graph nodes with backend capability diagnostics. |
| 14 | Multi-output routing | Same scene/profile renders once, encodes once, and fans out to preview/MP4/RTMP when profile-compatible. |
| 15 | Product performance suite | Product performance requires sustained render, encode, decode, MP4, and RTMP workloads with render latency, encode latency, dropped frames, CPU, RAM, and VRAM estimates. |
| 16 | Fault recovery | Encoder failure, RTMP disconnect, MP4 finalize failure, decode EOF/seek failure, source loss, device reset, and export failure isolate affected routes. |
| 17 | Backend contract freeze | Core contracts stabilize for Windows/Linux/macOS adapters. |
| 18 | Linux/macOS parity plan | Platform projects define VAAPI/DRM/DMABUF/Vulkan Video and VideoToolbox/CVPixelBuffer/IOSurface paths without contaminating Core. |

## Feature Readiness Matrix

| Feature | Status | Current truth |
|---|---|---|
| Vulkan composition | Supported | Multi-layer, nested canvas, solid, chroma, color, blur, text, transform, transitions for validated scope. |
| Offscreen output | Supported | Product GPU surface output. |
| Preview panel sink | Supported | Presents completed Vulkan GPU surfaces to Win32 panel without CPU readback. |
| Static image source | Supported | Windows PNG/JPEG static asset load path. |
| MP4 recording | Hardware-dependent | Composite capability promotes to `Supported` only when hardware encode, render-to-encode, MP4 recording, and MP4 output proofs pass. |
| RTMP streaming | Hardware-dependent experimental | Composite capability promotes to `Experimental` only when hardware encode, render-to-encode, and RTMP network proofs pass. |
| MP4 video input | Unavailable until proofs pass | Real decoder/provider exists, but product capability requires hardware decode, decode-to-render, and MP4 input proofs. |
| Desktop capture | Experimental | Product hardening for reset/reconnect and multi-display remains active. |
| Window capture | Planned | Windows Graphics Capture provider is not product implemented. |
| Webcam | Planned | Product source with immediate GPU upload remains active work. |
| NDI | Unsupported | Requires license approval and GPU-safe source/output design. |
| SRT/HLS/virtual camera/audio | Planned | Out of scope until core video pipeline is sustained. |
| RenderGraph physical executor | Active work | Logical plan exists; physical GPU pass executor is not complete. |
| Performance suite | Active work | Synthetic coverage exists; sustained real workloads are required. |

## Acceptance Suites

Required for normal implementation work:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

Required when touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex,
registry, render thread, providers, submissions, export, encode, decode, media
routes, or capability promotion:

```powershell
./scripts/test.ps1 -Tier Gpu
./scripts/verify-media-transport-rules.ps1
./scripts/verify-license-policy.ps1
./scripts/verify-engine-readiness-v12.ps1
```

Required before product media promotion:

```powershell
./scripts/test.ps1 -Tier Performance
./scripts/generate-media-proof-report.ps1
./scripts/verify-engine-readiness-v12.ps1 -RequireHardwareMedia
```

`verify-engine-readiness-v12.ps1` is the current official entrypoint. Normal
developer runs may pass with honest `Unavailable` statuses caused by missing
hardware/driver/API support. Release hardware runs use `-RequireHardwareMedia`
and fail when required proof chains do not pass.

## Capability And Proof Policy

Composite product capabilities:

- MP4 recording requires:
  `proof.hardware_encode.h264` (Hardware H.264 encode proof),
  `proof.render_to_encode.gpu` (Render-to-encode proof),
  `proof.recording.mp4.h264` (MP4 recording proof),
  `proof.media_io.mp4_output.product` (MP4 output product proof).
- RTMP H.264 requires:
  `proof.hardware_encode.h264`,
  `proof.render_to_encode.gpu`,
  `proof.media_io.rtmp_output.network` (RTMP network output proof).
- MP4 video input requires:
  `proof.hardware_decode.h264`,
  `proof.decode_to_render.gpu` (Decode-to-render proof),
  `proof.media_io.mp4_input.product` (MP4 input product proof).
- Webcam requires:
  `proof.media_io.webcam_input.product` (Webcam input product proof).
- NDI requires license approval and product proofs for input/output separately.
  The output side is represented by the NDI output product proof.

Non-passed proofs must include a reason. Passed proofs must identify backend
evidence. Product media proofs require `BackendOutputValidated` evidence except
render-to-encode, which may use `BackendCallSucceeded` for the export/conversion
boundary.

## Platform Parity Plan

Windows is the first product backend:

- D3D11/D3D11VA/Media Foundation for H.264 decode/encode.
- Vulkan renderer with D3D11 shared texture import/export.
- Desktop duplication and Windows Graphics Capture for capture.
- Media Foundation webcam capture with immediate GPU upload where necessary.

Linux planned adapters:

- VAAPI/DRM PRIME/DMABUF and Vulkan import/export.
- Vulkan Video where runtime capability and driver support are sufficient.
- Vendor SDK paths only in Linux-specific projects after license/API review.

macOS planned adapters:

- VideoToolbox decode/encode.
- CVPixelBuffer/IOSurface/Metal bridge.
- Vulkan/Metal interop only through isolated platform adapters.

## Known Technical Debt

- MP4/RTMP proof paths are now real but must graduate from short proof runs to
  sustained engine route tests and performance gates.
- Decode proof and MP4 video input need sustained validation on hardware that
  returns `IMFDXGIBuffer` GPU samples.
- RenderGraph remains logical until it owns physical GPU pass scheduling,
  resource pooling, effect intermediates, and encoded route fanout.
- Fault recovery needs product tests that inject encoder, decoder, export,
  source, sink, network, and device failures.
- Docs outside this roadmap may contain historical gate wording; current work
  must prefer this document and update stale text when touched.
