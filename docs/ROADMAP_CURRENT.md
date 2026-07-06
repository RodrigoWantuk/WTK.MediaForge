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
- Full pipeline product foundation: scene/source/output helpers, multi-scene routing contracts, package/preset serialization contracts, and render-graph planning tests.

Acceptance records:

- `docs/CP2_ACCEPTANCE.md`
- `docs/CP3_SOLID_ACCEPTANCE.md`
- `docs/CP3_NESTED_ACCEPTANCE.md`
- `docs/CP3_CHROMA_ACCEPTANCE.md`

## Active vNext Commit Order (GPU Media Law)

Execute in this exact order. One commit unit per implementation session.

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
| 06 | Windows Hardware Decode MVP | Gpu | **Done** |
| 07 | Video Source Runtime | Fast | **Done** |
| 08 | Texture Streaming | Gpu | **Done** |
| 09 | Renderer Video Integration | Gpu | **Done** |
| 10 | Scene Runtime | Fast + Gpu | **Done** |
| 11 | Render Graph (executor) | Gpu | **Done** |
| 12 | GPU Effects Framework | Gpu | **Done** |
| 13 | Transform Effects | Gpu | **Done** |
| 14 | Text Rendering | Gpu | **Done** |
| 15 | Hardware Encode Foundation | Gpu; requires 04 | **Done** |
| 16 | MP4 Recording MVP | Gpu | **Done** |
| 17 | RTMP Output MVP | Gpu | **Done** |
| 18 | Performance Validation | Report | **Done** |
| 19 | Fault Recovery | Gpu stress | **Done** |
| 20 | Engine Readiness Gate | Fast + Gpu + verify | **Done** |

### Phase 2 blocking rules

- Do not implement hardware MP4/RTMP/recording until Commit 04 end-to-end export proof passes.
- All engine textures are acquired through `GpuResourcePool` / `VulkanGpuResourcePool`; no ad-hoc `VulkanOffscreenRenderTarget` construction in product paths.
- Sinks never invoke render; `FrameScheduler` owns frame ordering (Commit 02+).
- Physical GPU dispose is deferred to pool retirement; invalidate/recycle does not imply immediate `VkImage` destroy.
