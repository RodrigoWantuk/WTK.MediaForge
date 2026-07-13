# Current Roadmap

This roadmap is mandatory. Do not choose a different order inside the active
vNext GPU media track. Historical acceptance details live in the CP2/CP3
acceptance reports. Long-term product planning lives in
`docs/FULL_PIPELINE_ROADMAP.md`.

## Current Status

Complete foundations:

- P0 GPU lifecycle hardening.
- Engine transactional/shutdown hardening.
- Product model and public API foundations.
- Source runtime/buffer foundation.
- Public sink queue/fanout foundation.
- CP1 visual correctness for the first source/offscreen path.
- CP2 multi-layer Vulkan composition.
- CP3 solid layer, nested canvas, and first `ChromaKeyEffect`.
- First public visual sink through `CpuReadbackSink` for debug/sample/validation.
- `PreviewPanelSink` lifecycle hardening; still experimental pending local reliability.
- Intermediate target pool and Vulkan readback staging pool.
- Transform/crop/rotation/pivot support in the Vulkan composition path.
- Windows PNG/JPEG static image product path using load-time decode and D3D11 shared texture GPU leases.
- Desktop duplication capture reconnect now retires old D3D11 slot rings and
  stops/disposes superseded or failed duplication sessions instead of leaking
  native capture resources during recovery.
- Decoded GPU frame to render-source frame bridge, keeping `GpuTextureLease`
  as the internal resource lease and `GpuFrameLease` as the render source
  contract.
- Windows video-file source provider scaffold wired to `VideoSourceRuntime`
  behind an internal prototype opt-in; default Windows engine registration and
  capability reports still keep video file sources unavailable for product use.
- Media Foundation H.264 encoder product boundary is explicit: the public path
  now uses typed `HardwareVideoEncoderSettings`, a shared Media Foundation
  runtime lease, a persistent hardware MFT session, and a Windows H.264 proof
  runner. Capability reports can merge proof-runner results, but MP4/RTMP
  product availability still requires the full render-to-encode and output
  product proofs.
- Encoder format conversion has an explicit contract. Vulkan/D3D11 export now
  requires pixel-format compatibility, and BGRA/RGBA to NV12 conversion has a
  D3D11 VideoProcessor GPU path with explicit unavailable diagnostics for
  unsupported devices/sources; CPU staging fallback is prohibited.
- Encoded output sinks are separated from render output sinks:
  `RecordingMp4PacketSink` and `RtmpPacketSink` consume `EncodedVideoPacket`
  only, while render sinks remain surface consumers.
- Encoded packet metadata is explicit: H.264 packets declare Annex-B or AVCC
  bitstream format, optional duration, and optional codec configuration. The
  prototype MP4/RTMP consumers reject unknown bitstream format and do not
  fabricate codec configuration.
- Public RTMP packet sinks now follow the same product evidence rule as MP4:
  they reject packets without trusted `BackendOutputValidated` evidence unless
  a test-only prototype transport is explicitly opted in.
- Encoded packet fanout has per-consumer backpressure policies and write
  timeouts. Recording paths use bounded backpressure; network paths fail the
  affected output instead of blocking render or encode threads indefinitely.
- Encoded output routes now have an explicit factory boundary and runtime
  status snapshots. The Windows facade recognizes MP4 recording and RTMP as
  encoded routes, but refuses to start them until the composed hardware media
  proofs promote the capability. Recording routes use queue/backpressure
  semantics and fail observably if frames would be silently dropped.
- Composite product capability promotion is owned by
  `CapabilityProofAggregator`: MP4 recording requires hardware encode,
  render-to-encode, and MP4 output product proofs; RTMP requires hardware
  encode, render-to-encode, and RTMP network proof; MP4 video input requires
  hardware decode and decode-to-render proofs.
- Rendered-output-to-encoder input preparation is explicit. A rendered surface
  is exported directly only when the encoder requirement matches; otherwise the
  path must use a GPU-only conversion step such as BGRA/RGBA -> NV12. If no
  GPU converter exists, the product path fails instead of staging through CPU.
- Rendered-output encoder preparation now rejects ambiguous or incompatible
  conversion results: GPU format conversion must return a new GPU lease with
  the exact requested size/format, or the product path fails before encoding.
- Hardware media proof execution now has a session registry that can run
  concrete proof runners and merge their results into the capability report.
  This keeps static capability declarations separate from proof results
  observed on the current machine. `MediaForgeWindows` registers the Windows
  H.264 hardware encode proof runner without running it during the cheap
  static probe.
- Media Foundation file decode now has a real product session boundary:
  SourceReader is opened with a D3D11 device manager, DXVA enabled, NV12 output
  requested, and decoded frames are accepted only when Media Foundation returns
  an `IMFDXGIBuffer` GPU texture. The texture is copied by GPU into a D3D11
  shared texture lease for renderer import. System-memory samples and
  placeholder texture output remain unavailable for product decode.
- Decode-to-render product proof now has an audit gate and remains blocked until
  hardware decode, source-frame adaptation, and renderer submission all provide
  `BackendOutputValidated` evidence.
- RenderGraph execution now propagates available source-frame resources and
  explicit skip reasons through the logical graph, and submitted
  `RenderFrameSnapshot` instances carry the graph execution result computed
  after source leases are acquired; real GPU pass execution and output texture
  production remain future work.
- Vulkan source-layer color correction now applies brightness, contrast,
  saturation, and hue in the shader before chroma key.
- Vulkan source-layer blur is product-validated for the current scope: a
  source layer is rendered to a pooled intermediate target, blurred with
  horizontal/vertical shader passes, and composited back into the canvas.
- Vulkan text rendering now uses a rasterized glyph atlas uploaded to GPU
  texture memory; the Vulkan project owns atlas upload/rendering only, while
  OS-specific adapters own font rasterization. Current product validation
  covers Windows Vulkan text layers through the Windows rasterizer adapter with
  explicit `FontFamily` snapshot/API propagation.
- Output route transitions now support product-validated cut/fade behavior for
  routed output changes. The current Vulkan implementation crossfades previous
  and current canvas targets in the output pass and is covered by pixel tests.
- Full pipeline product foundation: scene/source/output helpers, multi-scene routing contracts, package/preset serialization contracts, and render-graph planning tests.

Acceptance records:

- `docs/CP2_ACCEPTANCE.md`
- `docs/CP3_SOLID_ACCEPTANCE.md`
- `docs/CP3_NESTED_ACCEPTANCE.md`
- `docs/CP3_CHROMA_ACCEPTANCE.md`
- `docs/PREVIEW_PANEL_ACCEPTANCE.md`

## Active vNext Commit Order (GPU Media Law)

Execute in this exact order. One commit unit per implementation session.
Before resuming Commit 11, execute the **Active vNext v3 Truth Gate** below so
prototype/skeleton work cannot be promoted as product capability by mistake.

Hardware decode and encode are mandatory for continuous video. The product path
must keep decompressed frames in GPU/VRAM; if Windows, Linux, or macOS cannot
provide a validated hardware path for a codec/device, that capability remains
unavailable instead of using software fallback. OS-specific media adapters must
stay in OS-specific projects.

| # | Commit | Gate |
|---|--------|------|
| 00 | Docs GPU media law + FFmpeg policy | |
| 01 | Capability/license matrix + `GetCapabilityReportAsync` | Studio/API consumable |
| 02 | Media transport types + audit contracts | |
| 03 | Guard rails (allowlist + scanner) | Fast tier |
| 04 | Source/output descriptors + registry | |
| 05 | `RenderFrameContext` temporal | |
| 06 | **Windows GPU export proof** | **Blocks MP4/RTMP if failed** |
| 07 | Lifecycle rollback hardening | |
| 08 | Preview reliability gate | |
| 09 | Transform/crop/rotation/pivot | |
| 10 | Static image PNG/JPEG product path | WebP Planned |
| 11 | Text rendering product implementation (glyph atlas GPU) | |
| 12 | Effect chain GPU (color + blur) | |
| 13 | Output route transitions | |
| 14 | Desktop/window capture reliability | |
| 15 | Webcam product path (`WebcamSystemRawInput` exception) | |
| 16 | Hardware decode boundary | |
| 17 | Hardware encoder abstraction (MF probe real) | Requires Commit 06 |
| 18 | Windows MF H.264 hardware MP4 product path | No FFmpeg/libx264 |
| 19 | RTMP experimental (encoded packets) | SRT Planned/blocked |
| 20 | Output sink compliance | |
| 21 | Engine media telemetry | |
| 22 | Linux skeleton | |
| 23 | macOS skeleton | |
| 24 | Documentation + CI gate closure | |

### Blocking rules

- Do not implement hardware MP4/RTMP until Commit 06 export proof passes.
- Do not use FFmpeg, libx264, or software encode in the first hardware MP4/RTMP product path.
- Do not use software decode/encode fallback for continuous video on any platform.
- Do not mark a vendor/backend as available unless runtime probing validates
  GPU surface input/output and the capability report includes non-prototype
  readiness.
- Do not treat static image load as a raw CPU video exception.
- NVENC/QSV/AMF direct SDK paths remain Planned until post-MF hardware product path license review.
- SRT remains Planned/blocked until license and transport design review.

## Parallel Studio UI Track

A limited Avalonia Studio UI track may run in parallel when it stays inside the
UI/mock scope documented in `docs/STUDIO_UI_RECOVERY_PLAN.md`.

Studio may consume `GetCapabilityReportAsync()` in background to show
Supported/Planned/Unsupported status with reasons. Do not show recording,
streaming, or sources as functional when capability report says otherwise.

Still blocked in Studio until runtime gates open:

- real webcam, desktop/window, media file, animated image, Lottie, NDI, RTSP/IP camera adapters;
- real encoded file, RTMP, NDI, or virtual-camera outputs;
- real audio capture, mixer, mux, or equalization;
- product preview integration beyond approved `PreviewPanelSink` reliability work.

## Validation Gates

After each implementation unit:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

When touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry,
render thread, provider, submission, or GPU export/encode paths, also run:

```powershell
./scripts/test.ps1 -Tier Gpu
./scripts/verify-media-transport-rules.ps1
./scripts/verify-license-policy.ps1
./scripts/verify-engine-readiness-v9.ps1
```

Before promoting media transport, encoder, decoder, render-output encode, sink,
or capability work, run the full product boundary suite:

```powershell
./scripts/verify-engine-readiness-v10.ps1
./scripts/verify-engine-readiness-v11.ps1
```

## Active Phase 2 Commit Order (GPU Pipeline Completo)

Execute in this exact order after vNext (commits 00-24) is complete. Plan:
`.cursor/plans/phase2_gpu_engine_evolution.plan.md`.

| # | Commit | Gate | Status |
|---|--------|------|--------|
| 01 | GPU Resource Lifetime (`GpuResourcePool`, `GpuTextureLease`) | Fast + Gpu | **Done** |
| 02 | GPU Frame Scheduler | Fast | **Done** |
| 03 | Asset Manager | Fast | **Done** |
| 04 | **GPU Surface Export Proof (Real)** | **Blocks 15-17** | **Done** |
| 05 | Hardware Decode Foundation | Fast | **Done** |
| 06 | Windows Hardware Decode Boundary | Gpu | **Backend work started: SourceReader/D3D11VA session accepts only IMFDXGIBuffer GPU samples; product proof still pending** |
| 07 | Video Source Runtime | Fast | **Done** |
| 08 | Texture Streaming | Gpu | **Done** |
| 09 | Renderer Video Integration | Gpu | **Done** |
| 10 | Scene Runtime | Fast + Gpu | **Done** |
| 11 | Render Graph (executor) | Gpu | **Done:Contract/Skeleton - not a GPU pass executor** |
| 12 | GPU Effects Framework | Gpu | **Color correction and source-layer blur ProductValidated in Vulkan source/effect passes** |
| 13 | Transform Effects | Gpu | **Done:ProductValidated for Vulkan geometry/shader path; graph nodes remain skeleton** |
| 14 | Text Rendering | Gpu | **Done:ProductValidated for Windows Vulkan glyph atlas text layers** |
| 15 | Hardware Encode Foundation | Gpu; requires 04 | **Backend work started: MF hardware MFT session/settings/proof runner exist; MP4/RTMP product proof still pending** |
| 16 | MP4 Recording Packet Mux Boundary | Gpu | **Contract/Product boundary - public sink requires BackendOutputValidated packets; end-to-end recording remains blocked by hardware encoder proof** |
| 17 | RTMP Network Transport Boundary | Gpu | **Contract/Product boundary - TCP RTMP handshake/publish and FLV H.264 packetization implemented; end-to-end streaming remains blocked by hardware encoder proof** |
| 18 | Synthetic Performance Validation | Report | **NeedsRealBackend - synthetic workload only** |
| 19 | Fault Recovery | Gpu stress | **Done:Contract - integration with real failure points pending** |
| 20 | Engine Readiness Gate | Fast + Gpu + verify | **Done:Contract** |

### Phase 2 blocking rules

- Do not implement hardware MP4/RTMP/recording until Commit 04 end-to-end export proof passes.
- All engine textures are acquired through `GpuResourcePool` / `VulkanGpuResourcePool`; no ad-hoc `VulkanOffscreenRenderTarget` construction in product paths.
- Sinks never invoke render; `FrameScheduler` owns frame ordering (Commit 02+).
- Physical GPU dispose is deferred to pool retirement; invalidate/recycle does not imply immediate `VkImage` destroy.

## Active vNext Correction Track

The current engine must treat MP4 recording, RTMP streaming, Windows hardware
decode, Windows hardware encode, and performance validation as prototype
infrastructure until backend work proves real GPU media flow. Product capability
reports must use `PrototypeOnly`, `Blocked`, or `Planned` for these paths and
must not expose them as user-available features.

Required correction order:

1. Capability truth reset.
2. Audit evidence hardening so canned packets or placeholder textures cannot
   satisfy product proof.
3. Encode scheduler timing, cancellation, and backpressure correctness.
4. Real Media Foundation hardware encoder/decode backend work, or explicit
   unavailable capability.
5. Real decode -> render -> encode proof before MP4/RTMP can become
Experimental or Supported.

## Active vNext v8 Hardware Media And I/O Proof Set

The hardware media proof set is implemented by
`./scripts/verify-engine-readiness-v8.ps1` and is included from the newer v9/v10
readiness gates.

The v8 proof set is explicit and capability-driven. It keeps codec/backend
proofs separate from product I/O proofs so MP4, RTMP, webcam, and NDI cannot be
advertised as ready merely because one internal prototype path exists.

| Proof | Capability id | Required evidence |
|---|---|---|
| Render-to-encode proof | `proof.render_to_encode.gpu` | Rendered GPU output reaches encoder input with `BackendCallSucceeded` evidence and without CPU readback/staging. |
| Hardware encode proof | `proof.hardware_encode.h264` | Platform H.264 hardware encoder produces `EncodedVideoPacket` with `BackendOutputValidated` evidence. |
| MP4 recording proof | `proof.recording.mp4.h264` | Public MP4 sink writes a real packet-only MP4 from hardware-validated H.264 packets. |
| Hardware decode proof | `proof.hardware_decode.h264` | Platform H.264 hardware decoder produces GPU-backed decoded frames with `BackendOutputValidated` evidence. |
| Decode-to-render proof | `proof.decode_to_render.gpu` | Decoded GPU frame is imported/rendered by the compositor without CPU staging/readback. |
| MP4 output product proof | `proof.media_io.mp4_output.product` | Rendered output is hardware-encoded and muxed into a real MP4 file with backend-validated packet evidence. |
| MP4 input product proof | `proof.media_io.mp4_input.product` | A real MP4 file is demuxed/decoded by hardware into GPU surfaces and rendered without CPU frame transport. |
| Webcam input product proof | `proof.media_io.webcam_input.product` | Webcam frames cross any required OS raw boundary once, immediately upload to GPU, use bounded live buffering, and publish GPU leases. |
| RTMP network output proof | `proof.media_io.rtmp_output.network` | Hardware-encoded packets are packetized and sent through a real network RTMP transport without blocking the render thread. |
| NDI input product proof | `proof.media_io.ndi_input.product` | NDI licensing is approved and the input path is GPU-safe without continuous CPU frame transport. |
| NDI output product proof | `proof.media_io.ndi_output.product` | NDI licensing is approved and output avoids continuous CPU readback while preserving sink backpressure/lifetime contracts. |

Default CI may report proofs as `Unavailable` with reasons when the hardware
path is not implemented or not present. Release/readiness machines use the
current readiness gate with `-RequireHardwareMedia`; that mode fails unless
every required v8 hardware media proof is `Passed`.

## Active vNext v3 Truth Gate

Product readiness is tracked separately from user-facing support status:

- `Contract`: API/lifetime/scheduling contract exists, but does not prove a product feature.
- `Skeleton`: placeholder or structural implementation exists and must not be advertised as available.
- `Prototype`: internal proof or fake/prototype backend exists and must not be advertised as available.
- `BackendCallSucceeded`: a backend call/export proof succeeded, but full product output is not validated.
- `ProductValidated`: the feature has product-level behavior and tests for its current scope.

Current truth table:

| Area | Product readiness |
|---|---|
| Resource lifetime | Done:Contract |
| Frame scheduler | Done:Contract |
| Asset manager | Done:Contract |
| Static image Windows PNG/JPEG | Done:ProductValidated |
| Export surface proof | Done:BackendCallSucceeded, not ProductValidated |
| Decode-to-source frame bridge | Done:Contract |
| Windows video-file source provider | Done:Prototype, blocked by default |
| Webcam source | Planned until immediate GPU-upload provider is product validated |
| Windows decode | Backend work started; SourceReader/D3D11VA path requires `IMFDXGIBuffer` GPU samples and rejects CPU samples; product proof pending |
| Decode-to-render proof | Blocked until real decode backend is validated |
| Windows encode | Backend work started; typed settings, MF runtime lease, product MFT session, and Windows H.264 proof runner exist; product output proofs pending |
| Encoder format conversion | Done:BackendCallSucceeded for D3D11 VideoProcessor path when supported; product encode remains blocked on real MF packet validation |
| Packet sink boundary | Done:Contract with explicit bitstream metadata |
| MP4 writer | Done:Contract/Product boundary; public path requires trusted BackendOutputValidated H.264 packet evidence and rejects prototype/contract-only packets |
| RTMP transport | Done:Contract/Product boundary; public path requires trusted BackendOutputValidated H.264 packet evidence, rejects prototype/contract-only packets, and keeps prototype transport behind explicit test opt-in |
| RenderGraph | Done:Contract/resource bridge; not a GPU pass executor |
| Color correction effect | Done:ProductValidated for Vulkan source-layer shader |
| Blur effect | Done:ProductValidated for Vulkan source-layer shader/intermediate passes |
| Text rendering | Done:ProductValidated for Windows Vulkan glyph atlas upload |
| Output route transitions | Done:ProductValidated for Vulkan cut/fade output pass |
| Performance validation | Done:Skeleton |
| Fault recovery | Done:Contract |

`CapabilityEntry.ProductReadinessStatus` enforces this split: entries marked
`Prototype` or `Skeleton` cannot be emitted as `Supported` or `Experimental`.
The executable guard for this truth table is now
`./scripts/verify-engine-readiness-v9.ps1`. Use
`./scripts/verify-engine-readiness-v10.ps1` for the full local readiness run
that includes GPU and Performance tiers. Release hardware validation still uses
`-RequireHardwareMedia`; hardware proof absence must remain explicit
`Unavailable` and must never become software fallback.

## Active vNext v9/v10/v11 Product Boundary Gates

`./scripts/verify-engine-readiness-v9.ps1` is the default product-boundary
gate. It runs build, Fast tier, media transport guard rails, license guard
rails, and product-boundary tests for capability truth, render-output encode
preparation, encoded sink evidence, Windows media boundaries, and docs.

`./scripts/verify-engine-readiness-v10.ps1` extends v9 with GPU and Performance
tiers. Use it before promoting any media transport, encoder, decoder, render,
sink, or capability work beyond contract/prototype status.

`./scripts/verify-engine-readiness-v11.ps1` extends v10 with the current v6
media-runtime checks: hardware media proof-set execution, capability proof
aggregation tests, encoded output route/status/backpressure tests, and Windows
media proof truth tests. It is the preferred local gate before changing MP4,
RTMP, decode, encode, or encoded-route capability behavior.

Hardware proof runners are registered through `HardwareMediaProofRegistry`.
They may report `Unavailable` on developer/CI machines without required
hardware, but product release validation must run with `-RequireHardwareMedia`
and fail unless required proof entries are `Passed`.

## Future Phase - FFmpeg Libraries Integration Review

This phase is intentionally scheduled **after** the first native hardware MP4/RTMP product path.

FFmpeg is not used in the first recording or streaming product path. The first product path must prove the native GPU-safe media path:

```text
GPU rendered output
  -> platform hardware encoder
  -> EncodedVideoPacket
  -> native muxer / packetizer / stream transport
```

The purpose of this future phase is to evaluate whether selected FFmpeg libraries can be used safely and legally for container-level and encoded-packet-level work, without violating the GPU Media Law.

### Scope

Allowed evaluation:

- demuxing encoded packets from containers or network streams;
- muxing already encoded packets into containers;
- reading container metadata;
- probing stream/container information;
- codec parser or bitstream filter usage when it does not perform software decode/encode;
- auxiliary `libavutil` usage required by approved packet/container workflows.

Explicitly out of scope:

- `ffmpeg.exe` execution as a product path;
- rawvideo pipe;
- software video decode;
- software video encode;
- raw `AVFrame` processing in product runtime;
- raw YUV/RGBA frame transport through CPU/RAM;
- GPL FFmpeg builds;
- nonfree FFmpeg builds;
- `libx264`;
- `libx265`;
- software fallback when hardware decode/encode is unavailable.

### Entry criteria

This phase cannot start until all of the following are true:

- GPU Media Law contracts are implemented;
- media transport guard rails are active;
- license policy guard rails are active;
- hardware encoder path is proven;
- MP4 recording product path works through hardware encode or is honestly marked unavailable;
- RTMP experimental output uses hardware encoded packets only;
- capability matrix exposes FFmpeg/libav entries as Planned / RequiresLegalReview / Prohibited;
- `docs/MEDIA_LICENSE_POLICY.md` contains the authoritative FFmpeg policy.

### Implementation rules

Future FFmpeg/libav integration must follow these rules:

- FFmpeg must be built/configured as LGPL-only;
- no `--enable-gpl`;
- no `--enable-nonfree`;
- no `libx264`;
- no `libx265`;
- no rawvideo pipe;
- no software encode/decode fallback;
- no decompressed video frame may cross CPU/RAM in normal product runtime;
- FFmpeg libraries may only operate on encoded packets, containers, metadata, codec configuration, or bitstream data;
- all usage must be reflected in `docs/GPU_MEDIA_SUPPORT_MATRIX.md`;
- all usage must be reflected in `docs/MEDIA_LICENSE_POLICY.md`;
- all usage must be exposed through the capability report.

### Accepted future architecture

```text
File / network stream
  -> FFmpeg/libav demux, encoded packets only
  -> EncodedVideoPacket
  -> platform hardware decoder
  -> GpuVideoFrameLease
  -> GPU composition
```

```text
GPU composition output
  -> platform hardware encoder
  -> EncodedVideoPacket
  -> FFmpeg/libav mux, encoded packets only
  -> output container
```

### Rejected architecture

```text
File / network stream
  -> FFmpeg software decode
  -> raw AVFrame in CPU RAM
  -> GPU upload
```

```text
GPU composition output
  -> CPU readback
  -> rawvideo pipe
  -> FFmpeg software encode
  -> output container
```

### Exit criteria

This phase is accepted only if:

- legal/license review approves the selected FFmpeg/libav usage;
- FFmpeg build configuration is documented;
- third-party notices are updated;
- no GPL or nonfree component is used;
- no raw video frame is transported through CPU/RAM;
- media transport guard rails pass;
- license policy guard rails pass;
- capability matrix accurately reports every FFmpeg/libav-backed feature;
- product code never falls back silently to software encode/decode.

### Roadmap status

Until this phase is explicitly started and completed:

| Item | Roadmap Status |
|---|---:|
| FFmpeg/libav demux | Planned / RequiresLegalReview |
| FFmpeg/libav mux | Planned / RequiresLegalReview |
| FFmpeg software decode | Prohibited |
| FFmpeg software encode | Prohibited |
| FFmpeg executable process | Prohibited |
| FFmpeg rawvideo pipe | Prohibited |
| GPL FFmpeg build | Prohibited |
| Nonfree FFmpeg build | Prohibited |
| `libx264` / `libx265` | Prohibited unless separate commercial licensing is approved |
