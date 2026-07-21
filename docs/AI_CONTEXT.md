# AI Context - WTK MediaForge

## Mission

WTK MediaForge is a GPU-first, cross-platform media composition engine. Windows
is the first real backend; Core must remain independent of Vulkan, D3D11,
Media Foundation, VAAPI, and VideoToolbox.

## Non-Negotiable Media Law

Continuous video decode and encode must use platform hardware acceleration.
Uncompressed continuous frames stay in GPU memory from decode/capture through
composition and encode/presentation. There is no software codec fallback.

CPU/RAM is allowed for encoded packets, metadata, commands, static-image
load-time decode, tests/debug readback, and a documented OS capture boundary
that immediately uploads into a bounded GPU slot.

## Product Model

```text
MediaForgeProject
  -> MediaForgeCanvas (public scene)
     -> layers: source, nested canvas, text, solid
     -> layer/scene effects
  -> MediaForgeRenderOutput
     -> one or more sinks
```

- Sources produce leased frames; they do not render.
- Layers are scene instances of reusable sources.
- Nested canvas references carry version binding and reject cycles/depth overflow.
- Live edits publish transactionally. Apply edits remain draft until commit.
- Sinks consume completed output; they never request a render.

## Runtime Path

```text
SourceRuntimeManager
  -> immutable RenderFrameSnapshot
  -> compiled physical RenderGraph plan
  -> MediaForgeRenderThread / Vulkan backend
  -> IRenderFrameSubmission
  -> completed RenderedOutputFrame leases
  -> preview sinks and/or shared encoded output groups
  -> GPU NV12 conversion -> hardware encoder -> validated packets
  -> MP4 / RTMP packet workers
```

Compatible encoded outputs share scene, dimensions, color space, H.264 profile,
FPS, bitrate, GOP, and pixel format. They render/convert/encode once and fan out
ref-counted packets. Recording never silently drops; RTMP uses explicit
drop/reconnect policy.

## Lifetime Contracts

- Submission cleanup is `WaitForCompletionAsync(timeout, cancellationToken)`
  followed by `DisposeCompleted()`.
- A command buffer retains every referenced framebuffer, descriptor set,
  temporary target, texture lease, snapshot, fence, and command resource until
  completion.
- Fence timeout preserves potentially in-flight resources. It cannot trigger
  physical destruction; retry or terminal failure owns them.
- Provider ownership transfers once to `SourceRuntimeManager`; unregister and
  engine shutdown await asynchronous physical cleanup.
- Native handles are never logical texture identity.
- `CanvasId` is logical authoring identity. Physical render/cache identity is a
  deterministic resolved key containing the effective scene version, draft
  session where applicable, and nested graph fingerprint. Never mint a new
  logical canvas id to materialize a version binding.
- Intermediate Vulkan targets are never borrowed by two in-flight submissions.
  Cache invalidation retires an active target and physical disposal waits for
  the post-fence submission lease to return.
- GPU, keyed-mutex, sink, provider, and shutdown waits always have timeouts.
- Finalization errors are observable and cannot be reported as success.

## Capability Truth

`IHardwareMediaCapabilityProbe.ProbeAsync` is asynchronous and never blocked on
the UI thread. `MediaForgeCapabilitySnapshot` is immutable and cached by adapter,
driver/device generation. Windows Vulkan and D3D11 interop uses matching adapter
LUID; adapter 0 is not an acceptable implicit product choice.

`Supported`/`Experimental` requires implementation plus the applicable proof
chain. Non-passed proofs require reasons. Product H.264 packets require trusted
`BackendOutputValidated` evidence.

The official validation is:

```powershell
./scripts/verify-engine-readiness-v14.ps1
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Historical readiness scripts are stored under `docs/history/readiness-scripts`
and must not be executed as current gates.

## Current Constraints

- Preview is experimental until hosted resize and timeout-recovery evidence passes.
- Physical RenderGraph execution is incomplete for source acquisition, all
  effect passes, encoded dispatch, and exclusive temporary-resource ownership.
- Vulkan canvas/effect/transition/output recording is controlled by a validated
  physical plan. Invalid topology, canvas identity, or output identity fails
  before command recording instead of being skipped.
- Full Vulkan/D3D11 device-lost recreation is not yet proven end to end.
- Window Capture uses `Direct3D11CaptureFramePool`, copies the WinRT frame
  GPU-to-GPU into an engine-owned shared D3D11 slot, and is capability-promoted
  only after its HWND-to-Vulkan product proof passes.
- Studio persists canonical projects and probes runtime capabilities, but native
  preview/output control stays gated by runtime readiness.
- NDI product video remains blocked because Standard SDK CPU framebuffer APIs do
  not satisfy the GPU media law.
- Audio, SRT, virtual camera, and FFmpeg are deferred.

## Platform Boundaries

- Windows: `net8.0-windows10.0.19041.0`, D3D11/DXGI, Windows Graphics
  Capture, D3D11VA, Media Foundation, and Vulkan interop.
- Linux planned: VAAPI, DRM PRIME/DMABUF, Vulkan import/export or Vulkan Video.
- macOS planned: VideoToolbox, CVPixelBuffer/IOSurface, Metal bridge.

Platform code belongs only in its platform project. Unsupported adapters report
an explicit reason instead of contaminating Core with fallback logic.

## Documentation Authority

Product truth is defined by `docs/ROADMAP_CURRENT.md`, this file,
`docs/PRODUCT_MODEL.md`, `docs/PUBLIC_API.md`, `docs/GPU_MEDIA_SUPPORT_MATRIX.md`, and
`docs/REVIEW_CHECKLIST.md`. License and transport policy documents remain
mandatory supplemental policy. Files under `docs/history` are non-normative.
