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
- Windows PNG/JPEG static image MVP using load-time decode and D3D11 shared texture GPU leases.
- Decoded GPU frame to render-source frame bridge, keeping `GpuTextureLease`
  as the internal resource lease and `GpuFrameLease` as the render source
  contract.
- Windows video-file source provider scaffold wired to `VideoSourceRuntime`
  behind an internal prototype opt-in; default Windows engine registration and
  capability reports still keep video file sources unavailable for product use.
- Full pipeline product foundation: scene/source/output helpers, multi-scene routing contracts, package/preset serialization contracts, and render-graph planning tests.

Acceptance records:

- `docs/CP2_ACCEPTANCE.md`
- `docs/CP3_SOLID_ACCEPTANCE.md`
- `docs/CP3_NESTED_ACCEPTANCE.md`
- `docs/CP3_CHROMA_ACCEPTANCE.md`

## Active vNext Commit Order (GPU Media Law)

Execute in this exact order. One commit unit per implementation session.
Before resuming Commit 11, execute the **Active vNext v3 Truth Gate** below so
prototype/skeleton work cannot be promoted as product capability by mistake.

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
| 10 | Static image PNG/JPEG MVP | WebP Planned |
| 11 | Text rendering MVP (glyph atlas GPU) | |
| 12 | Effect chain GPU (color + blur) | |
| 13 | Output route transitions | |
| 14 | Desktop/window capture reliability | |
| 15 | Webcam MVP (`WebcamSystemRawInput` exception) | |
| 16 | Hardware decode boundary | |
| 17 | Hardware encoder abstraction (MF probe real) | Requires Commit 06 |
| 18 | Windows MF H.264 hardware MP4 MVP | No FFmpeg/libx264 |
| 19 | RTMP experimental (encoded packets) | SRT Planned/blocked |
| 20 | Output sink compliance | |
| 21 | Engine media telemetry | |
| 22 | Linux skeleton | |
| 23 | macOS skeleton | |
| 24 | Documentation + CI gate closure | |

### Blocking rules

- Do not implement hardware MP4/RTMP until Commit 06 export proof passes.
- Do not use FFmpeg, libx264, or software encode in the MP4/RTMP MVP.
- Do not treat static image load as a raw CPU video exception.
- NVENC/QSV/AMF direct SDK paths remain Planned until post-MF MVP license review.
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
```

## Active Phase 2 Commit Order (GPU Pipeline Completo)

Execute in this exact order after vNext (commits 00–24) is complete. Plan:
`.cursor/plans/phase2_gpu_engine_evolution.plan.md`.

| # | Commit | Gate | Status |
|---|--------|------|--------|
| 01 | GPU Resource Lifetime (`GpuResourcePool`, `GpuTextureLease`) | Fast + Gpu | **Done** |
| 02 | GPU Frame Scheduler | Fast | **Done** |
| 03 | Asset Manager | Fast | **Done** |
| 04 | **GPU Surface Export Proof (Real)** | **Blocks 15–17** | **Done** |
| 05 | Hardware Decode Foundation | Fast | **Done** |
| 06 | Windows Hardware Decode Prototype | Gpu | **PrototypeOnly - needs real decode backend** |
| 07 | Video Source Runtime | Fast | **Done** |
| 08 | Texture Streaming | Gpu | **Done** |
| 09 | Renderer Video Integration | Gpu | **Done** |
| 10 | Scene Runtime | Fast + Gpu | **Done** |
| 11 | Render Graph (executor) | Gpu | **Done:Contract/Skeleton - not a GPU pass executor** |
| 12 | GPU Effects Framework | Gpu | **Done:Skeleton - color/blur passes still need real pixels** |
| 13 | Transform Effects | Gpu | **Done:ProductValidated for Vulkan geometry/shader path; graph nodes remain skeleton** |
| 14 | Text Rendering | Gpu | **Done:Prototype - synthetic atlas, real glyph rasterization pending** |
| 15 | Hardware Encode Foundation | Gpu; requires 04 | **PrototypeOnly - canned packets are not product proof** |
| 16 | MP4 Recording Prototype | Gpu | **PrototypeOnly - muxer not production-ready** |
| 17 | RTMP Output Prototype | Gpu | **PrototypeOnly - in-memory transport only** |
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
| Windows decode | Done:Prototype |
| Windows encode | Done:Prototype |
| MP4 writer | Done:Prototype |
| RTMP transport | Done:Prototype |
| RenderGraph | Done:Contract/Skeleton |
| Color/Blur effects | Done:Skeleton |
| Text rendering | Done:Prototype |
| Performance validation | Done:Skeleton |
| Fault recovery | Done:Contract |

`CapabilityEntry.ProductReadinessStatus` enforces this split: entries marked
`Prototype` or `Skeleton` cannot be emitted as `Supported` or `Experimental`.

## Future Phase — FFmpeg Libraries Integration Review

This phase is intentionally scheduled **after** the first native hardware MP4/RTMP MVP.

FFmpeg is not used in the first recording or streaming MVP. The first MVP must prove the native GPU-safe media path:

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
- MP4 recording MVP works through hardware encode or is honestly marked unavailable;
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
