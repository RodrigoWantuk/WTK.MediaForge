# WTK MediaForge Architecture

WTK MediaForge is a GPU-first media compositor and runtime. The product model
describes what users compose; the runtime model describes how frames, snapshots,
GPU resources, submissions, and sinks execute safely.

This document is intentionally concise. Product contracts live in
[docs/PRODUCT_MODEL.md](docs/PRODUCT_MODEL.md), public API boundaries live in
[docs/PUBLIC_API.md](docs/PUBLIC_API.md), the active execution order lives in
[docs/ROADMAP_CURRENT.md](docs/ROADMAP_CURRENT.md), and the long-term product map
lives in [docs/FULL_PIPELINE_ROADMAP.md](docs/FULL_PIPELINE_ROADMAP.md).

## Layer Boundary

Do not mix product objects with runtime/GPU objects.

```text
Product / Composition Model            Runtime / GPU Execution Model
--------------------------------        --------------------------------
MediaForgeProject                       CompositionRuntime
MediaForgeSourceDefinition              SourceRuntimeManager
MediaForgeCanvas                        ProjectStateSnapshot
MediaForgeDrawObject                    RenderFrameSnapshot
MediaForgeEffect                        MediaForgeRenderThread
MediaForgeRenderOutput                  PendingRenderSubmissionTracker
MediaForgeProjectEditor                 IRenderBackend
MediaForgeProjectBuilder                IRenderFrameSubmission
Public RenderOutputSink(s)              Vulkan/D3D11 backend resources
```

Product-layer rules:

- Sources produce frames; they do not render and do not know canvases, outputs, or sinks.
- Draw objects place sources, text, solids, or nested canvases onto a canvas.
- Outputs route a canvas to completed output frames.
- Sinks consume completed output frames and never trigger rendering directly.
- GPU leases, snapshots, command buffers, fences, Vulkan objects, and D3D11 objects stay out of public project JSON.

## Current Runtime Topology

The hardened frame path is:

```text
SourceRuntimeManager / SourceFrameBuffer
  -> ProjectStateSnapshot
  -> RenderFrameSnapshot
  -> MediaForgeRenderThread
  -> PendingRenderSubmissionTracker
  -> MediaForgeVulkanRenderer
  -> VulkanRenderFrameSubmission
  -> RenderOutputSinkDispatcher
  -> public RenderOutputSink(s)
```

Current completed runtime foundations:

- GPU lifecycle hardening.
- Transactional engine load/update/bind/unbind behavior.
- Continuous render pump with backpressure diagnostics.
- Source runtime ownership and frame-buffer lease policies.
- Public bounded sink queues and fanout leases.
- CP2 multi-layer Vulkan composition.
- CP3 solid, nested canvas, and first `ChromaKeyEffect` composition.
- Offscreen target pooling and readback staging pooling.
- Experimental `PreviewPanelSink` lifecycle hardening.
- Internal render-graph planning foundation.

`PreviewPanelSink` remains experimental until the preview local reliability
milestone is complete. Real webcam, RTSP/IP camera, MP4 timeline, NDI, encoded
file, streaming, virtual camera, UI shell, plugin, and audio work must follow
`docs/ROADMAP_CURRENT.md`.

## Render Graph Direction

The target per-frame graph is:

```text
Outputs/Sinks -> RenderOutput -> Canvas/Scene -> DrawObjects -> Sources -> Effects
```

The graph must deduplicate by stable product/runtime keys:

- same source frame: acquire once per frame
- same reusable source/effect chain: render once when independent of placement
- same canvas size/config/version: render once
- different output sizes/layouts: split only output-fit/presentation passes
- same output with multiple sinks: render once and fan out leases

The current render-graph compiler is a planning foundation and test target. It is
not yet the Vulkan execution scheduler.

## GPU Lifetime Contract

`CPU finished submitting` is not the same as `GPU finished using`.

The production submission contract is:

```text
Render thread acquires snapshot
  -> backend.Submit(snapshot) returns IRenderFrameSubmission
  -> PendingRenderSubmissionTracker owns pending submission
  -> completion waits use explicit timeout
  -> DisposeCompleted releases snapshot, source leases, command resources, output surfaces
```

Required rules:

- `IRenderFrameSubmission` does not implement `IDisposable` or `IAsyncDisposable`.
- Cleanup is always `WaitForCompletionAsync(timeout, cancellationToken)` then `DisposeCompleted()`.
- `IRenderBackend` exposes `WaitIdleAsync(timeout, cancellationToken)`, never synchronous `WaitIdle()`.
- `MediaForgeVulkanRenderer` is internal and created by `MediaForgeVulkanRenderBackendFactory`.
- Command-buffer resources live until the submitted fence completes.
- Framebuffers, descriptor sets, offscreen targets, source texture leases, command buffers, fences, and snapshots are retained by submission ownership until completion cleanup.
- Public sinks receive leases that preserve rendered output surface lifetime.

## D3D11/Vulkan Interop

Windows capture currently uses D3D11/DXGI as the practical capture and interop
layer, with Vulkan as the composition backend.

```text
D3D11 texture
  -> shared NT handle
  -> Vulkan external memory import
  -> Vulkan image/view/sampler
  -> shader composition
```

Interop rules:

- External texture identity is `VulkanExternalTextureKey = GpuTextureId + Width + Height + Format`.
- Do not deduplicate logical textures by native handles or `DangerousGetHandleForInterop`.
- Keyed mutex waits and GPU waits must use explicit timeout.
- Registry import creation occurs outside the global registry lock.
- Failed imports and waiters must produce observable diagnostics or exceptions.

## Output Model

The output path is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

`CpuReadbackSink` is for debug, samples, and validation. It is not the primary
preview, encoder, or streaming path. `PreviewPanelSink` is the experimental GPU
preview path and consumes completed Vulkan offscreen surfaces without CPU
readback.

Future encoder, streaming, NDI, and virtual camera sinks must consume completed
rendered outputs. They must not add direct renderer branches.

## Resource Rules

- UI-owned project objects never own Vulkan or D3D11 objects.
- Runtime snapshots are immutable for the render thread.
- Source frame leases survive until snapshots/submissions/sinks release them.
- Output surface leases survive until all sink consumers release them.
- Failed cleanup is reported; physical resource finalization failures are not swallowed.
- Slow sinks use explicit queue/backpressure policy and do not block the render thread.
- Failed or empty sources produce diagnostics and missing-frame behavior, not renderer crashes.

## Media Bridge Direction

The final media path is hardware-first and portable by design. Native media
adapters should produce/consume GPU-compatible surfaces where possible:

- Windows: D3D11/D3D11VA/Media Foundation where needed.
- Linux: VAAPI/DRM/Vulkan Video/CUDA where available.
- macOS: VideoToolbox/Metal/CVPixelBuffer bridge.

Audio is future contract only until the video pipeline is stable.

## GPU Media Transport Law

Uncompressed video pixels must stay in GPU/VRAM on the normal product path.
CPU/RAM may carry encoded media, static asset load buffers, metadata, commands,
and explicitly registered exceptions only.

### Formal media categories

| Category | Description | Product path |
|----------|-------------|--------------|
| `EncodedVideoPacket` | H.264/HEVC/AV1/VP9 NAL units, RTSP/MP4 samples | Allowed in CPU/RAM/network |
| `GpuVideoFrame` | Vulkan image, D3D11 texture, DXGI surface, HW decoder surface | Required for continuous video |
| `RawCpuVideoFrame` | `byte[]` BGRA/NV12, software `AVFrame`, per-frame CPU bitmap | **Prohibited** on product path |
| `StaticCpuImageAsset` | PNG/JPEG load decode | Allowed **only at load**; must upload to GPU and release CPU copy |

### Allowed path

```text
Encoded media (CPU/RAM/network/disk)
  -> hardware decode / GPU upload boundary
  -> GPU surface in VRAM
  -> GPU composition / effects
  -> GPU surface in VRAM
  -> hardware encode / GPU presentation / GPU sink
  -> encoded packets (CPU/RAM/network/disk)
```

### Prohibited path

```text
GPU surface -> readback -> raw RGBA/NV12 in RAM -> CPU processing / CPU encoder
```

### Registered exceptions

Continuous raw CPU video is allowed only when registered with
`RawCpuVideoFrameException` / `RawCpuVideoFrameExceptionAttribute` and kinds:
`PixelTestOnly`, `ManualScreenshotOnly`, `WebcamSystemRawInput`.

Static image load is **not** an exception; it uses `MediaTransportKind.StaticCpuAsset`.

### Output sinks

- `PreviewPanelSink`: GPU surface (product/experimental)
- `CpuReadbackSink`: debug/test/validation only (`DebugOnlyCpuReadback`)
- Recording/streaming sinks: encoded packets after hardware encode only

### FFmpeg policy

FFmpeg is **not used** in the first hardware MP4/RTMP MVP. Future FFmpeg
integration requires LGPL-only build, no GPL components, no libx264/libx265,
no rawvideo pipe, and license review.

### Recording gate

Commit 06 (Windows GPU surface export proof: Vulkan -> D3D11/MF encoder input)
is a blocking gate before hardware MP4 recording. If export proof fails,
recording remains blocked in the capability matrix.

## Test Tiers

| Tier | Command | Scope |
|------|---------|-------|
| Fast | `./scripts/test.ps1 -Tier Fast` | Core, Diagnostics, Composition |
| GPU | `./scripts/test.ps1 -Tier Gpu` | D3D11, Vulkan, Capture GPU tests |
| Stress | `./scripts/test.ps1 -Tier Stress` | Long-running stress coverage |

General validation after implementation units:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

Run GPU tier when touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex,
registry, render thread, provider, or submission code.
