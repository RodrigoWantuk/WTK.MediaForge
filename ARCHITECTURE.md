# WTK MediaForge Architecture

WTK MediaForge separates the serializable product model from runtime and native
GPU ownership. This document is normative together with the current roadmap,
AI context, public API, support matrix, and review checklist.

## Product Boundary

```text
MediaForgeProject
  Sources (reusable definitions)
  Canvases / Scenes
    Layers
      source | nested canvas | text | solid
      ordered effects
  RenderOutputs
    scene route, dimensions, color space, transition, settings
  Audio
    sources, graph nodes, buses, routes, sinks
```

Project JSON contains stable ids, schema versions, typed settings, transforms,
effects, scene dependencies, and routes. It never contains leases, native
handles, command buffers, fences, device objects, sink workers, or secrets in
secret-safe export modes.

## Runtime Boundary

```text
MediaForgeEngine facade
  lifecycle + transactional project edits
  SourceRuntimeManager
  SceneRuntime published/draft stores
  render scheduler/thread
  physical RenderGraph compiler/executor
  RenderOutputSinkDispatcher
  MediaPipelineRuntime / encoded groups
  FaultRecoveryCoordinator
```

`MediaForgeEngine` remains the public facade. Internal ownership is split by
lifecycle, scene editing, source orchestration, output routing, scheduling, and
recovery; none of those services expose platform GPU objects publicly.

## Audio Boundary

Audio is a global project graph independent from the visual graph. Video scenes
and outputs select audio routes but do not own physical capture. The portable
audio runtime uses pooled planar float32 blocks at 48 kHz, explicit timestamps,
bounded fan-out, immutable compiled plans and transactional swaps between
blocks. Real-time callbacks never block, allocate, await, take contested locks,
access disk, or call UI/sinks directly. Platform capture/playback belongs in
dedicated Windows/Linux adapter projects.

## Cross-platform Contract

Windows and Linux are mandatory development targets. New product behavior is
portable by default and is divided into platform-neutral contracts plus native
adapters.

Portable projects:

- target portable .NET frameworks;
- contain product model, orchestration, validation, capability contracts, and
  runtime behavior that is independent of native operating-system APIs;
- never reference `WTK.MediaForge.Windows` or another platform implementation
  project;
- include tests that compile and run on both Windows and Linux.

Platform projects:

- implement portable contracts for the native operating system;
- own native handles, API bindings, device discovery, interop, and capability
  probes;
- may have dedicated platform-only tests, but cannot replace portable coverage.

A feature that currently has only a Windows adapter must still expose a portable
contract and explicit capability-unavailable behavior on Linux. A Linux build is
not made green by cross-targeting a Windows project, excluding relevant portable
code, or silently skipping required behavior.

The automatic `cross-platform-ci` workflow is an architecture gate. Every commit
must pass the complete Windows solution build/test job and the maintained Linux
portable build/test job before it reaches `master`.

## Frame Path

```text
encoded/capture boundary
  -> hardware decode or immediate GPU upload
  -> GpuFrameLease
  -> immutable RenderFrameSnapshot
  -> compiled physical pass plan
  -> Vulkan submission
  -> completed RenderedOutputFrame lease
  -> preview and/or encoded output group
  -> GPU export + NV12 conversion
  -> hardware encoder
  -> BackendOutputValidated packet lease
  -> MP4 / RTMP sink workers
```

Sinks never call the renderer. A shared encoded group is keyed by published
scene, output size/layout, color space, codec profile, FPS, bitrate, GOP, and
pixel format. Compatible MP4/RTMP routes encode once and fan out packets.

## Scene Editing

- `Live`: mutations publish atomically and normal outputs observe the next frame.
- `Apply`: mutations remain in a draft project/version until commit.
- Nested canvases carry published, draft-session, or explicit-version binding.
- Dependency graphs reject cycles and depth overflow.
- Apply commit calculates affected parent canvases and output routes.
- Route transitions own old/new version graphs; they are not layer effects.

## Physical RenderGraph

The graph compiles source acquisition, source/effect intermediates, primitive
layers, canvas composition, nested canvases, route transitions, output passes,
fanout, and encoded dispatch. Cache keys include source frame number, published
version, complete effect fingerprint, placement where relevant, dimensions,
format, color space, layout, and letterbox color.

Vulkan currently executes physical output/canvas fanout and blur intermediate
operations. The v14 target is to make the compiled graph the sole renderer
input and sole owner of all temporary-resource scope.

Before Vulkan command recording, the physical plan is validated against the
render snapshot. Operation keys must be unique and topologically ordered, every
dependency and consumer must exist, canvas/output identities must resolve, and
each snapshot output must have exactly one physical output pass. Product engine
submissions arrive with the already executed compiled plan; direct compilation
at the Vulkan boundary exists only for internal low-level backend tests.

## GPU Lifetime

CPU submission completion is not GPU completion.

```text
Submit(snapshot)
  -> IRenderFrameSubmission
  -> pending tracker owns submission
  -> WaitForCompletionAsync(explicit timeout)
  -> DisposeCompleted()
```

Every command-buffer reference survives until fence completion: source leases,
framebuffers, descriptor sets, temporary/offscreen targets, command buffers,
fences, semaphores, snapshots, and downstream output surfaces. A timeout keeps
resources alive for retry or terminal failed ownership; it never destroys them.

Providers transfer ownership to `SourceRuntimeManager` once. Engine unregister,
stop, and dispose await provider cleanup. `RetiredGpuResourceManager` reports
resource id, age, attempt count, refcounts/active slots, and finalization error.

## Windows Interop

```text
D3D11 capture/decode texture
  <-> NT shared handle / keyed synchronization
  <-> Vulkan external memory image
  -> composition
  -> Vulkan export
  -> matching-LUID D3D11 texture / VideoProcessor NV12
  -> Media Foundation hardware MFT
```

Logical texture identity is `GpuTextureId + width + height + format`, never a
native handle. Vulkan and D3D11 adapters must match by LUID. Missing identity or
cross-GPU mismatch is an explicit unavailable/failure result, not adapter-0
fallback.

`WTK.MediaForge.Windows` and its Windows hosts target
`net8.0-windows10.0.19041.0`; portable Core/Composition/Vulkan contracts remain
`net8.0`. Window Capture owns each WinRT frame only long enough to copy it on
GPU into a keyed, engine-owned D3D11 slot. The WinRT frame-pool surface is never
published as an engine lease.

## Backpressure And Recovery

- Source live queues default to `KeepLatest`; timeline sources use timestamped
  bounded selection.
- Render thread never waits indefinitely for a sink or network.
- Recording overflow fails only that route; it does not silently drop.
- RTMP has bounded queue, explicit drop accounting, keyframe-aware reconnect,
  backoff, attempt limit, and cancellation.
- Recovery is serialized per device/source/output resource key.
- Source loss isolates/restarts that source, including providers that enter
  `Failed` between acquisition calls. RTMP failure does not stop MP4.
- Device lost recreates the backend and output bindings before health returns
  to `Healthy`; retained old resources remain owned until their completion
  contract permits physical disposal.

## Platform Projects

- Core/Composition: portable contracts and product/runtime logic.
- Vulkan/Remote: portable implementations and contracts unless a source file is
  explicitly isolated behind a platform adapter.
- Windows: D3D11/DXGI, D3D11VA, Media Foundation, Win32, and Windows Vulkan interop.
- Linux: Linux-native integration, including future VAAPI/DRM PRIME/DMABUF/Vulkan
  Video adapters.
- macOS: future VideoToolbox/CVPixelBuffer/IOSurface/Metal adapter.

No platform fallback is added to Core. Hardware absence is capability truth.
Dependencies must point from platform projects toward portable projects, never
from portable projects toward a platform implementation.

## Studio Boundary

Studio has explicit Design and Runtime composition. Runtime saves canonical
`MediaForgeProject` JSON and probes hardware asynchronously. Avalonia selection,
handles, grid, and safe-area overlays remain separate from the future native GPU
surface. Native preview/output controls stay disabled until their engine gates pass.
