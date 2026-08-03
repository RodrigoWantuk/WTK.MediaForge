# AI Context — WTK MediaForge

This file is the compact technical context for developers and coding agents. Read `docs/README.md` for document authority and `docs/ROADMAP_CURRENT.md` for current execution order.

## Mission

WTK MediaForge is a GPU-first real-time media composition engine with a native Avalonia Studio and cross-platform product architecture.

Windows currently owns the physical production video path. Core, Composition, Vulkan contracts, Audio, Remote, and Studio architecture must remain portable. Linux and macOS physical media adapters remain separate platform work.

## Non-negotiable video law

Continuous video decode and encode use hardware acceleration.
Continuous video decode and encode must use platform hardware acceleration.

Continuous uncompressed frames remain on GPU-backed surfaces from capture/decode through composition and encode/presentation. There is no product software codec fallback and no raw-video pipe.

CPU/RAM is allowed for:

- encoded packets;
- metadata and commands;
- static-image load-time decode before GPU upload;
- tests/debug readback;
- a documented unavoidable OS capture boundary that immediately copies into a bounded GPU slot.

A raw continuous video frame must not circulate through Core, Composition, Studio, sinks, or product routing.

## Product model

```text
MediaForgeProject
  -> SourceDefinitions
  -> Canvases / Scenes
     -> source layers
     -> text and solid primitives
     -> nested canvases
     -> ordered effects
  -> RenderOutputs
     -> preview and/or encoded sinks
  -> Audio graph
     -> sources, nodes, buses, routes, sinks
```

- `MediaForgeCanvas` is the canonical scene object.
- Sources produce leased frames and do not render.
- Draw objects describe intent and do not own native resources.
- Sinks consume completed output leases or validated encoded packets and never request rendering.
- Transitions belong to output routes, not permanent layer effects.
- `MediaForgeProject` is the sole persisted project root.

## Scene editing

Scene editing belongs to the engine.

- `Live` publishes validated mutations transactionally.
- Rejected Live mutations preserve the last valid published scene.
- `Apply` isolates mutations in a draft session until commit.
- `Discard` removes the draft without changing published output.
- Nested canvases carry published, draft-session, or explicit-version bindings.
- Cycles and depth overflow are invalid.
- Apply computes direct/transitive parent dependencies and affected output ids.
- Studio trusts only engine-reported affected output ids.
- Route transitions own old/new explicit version graphs.

Scene history retains the latest bounded versions plus pinned versions and recursively pinned explicit dependencies. Drafts and transitions own explicit pin handles and release them deterministically.

## Runtime path

```text
SourceRuntimeManager
  -> leased source frames
  -> immutable RenderFrameSnapshot
  -> logical RenderGraph
  -> validated physical RenderGraph
  -> Vulkan backend submission
  -> completed rendered-output leases
  -> preview sinks and/or shared encoded groups
  -> GPU NV12 conversion
  -> hardware H.264 encoder
  -> validated encoded-packet leases
  -> MP4 / RTMP sink workers
```

Compatible MP4/RTMP routes share rendered pixels, conversion, and encoder configuration. Sinks retain independent bounded queues, lifecycle, and failure state.

Compatibility is separated into:

- rendered-pixel identity;
- encoder identity;
- sink/destination identity.

Destination secrets do not participate directly in public or logged identity.

## Physical RenderGraph

The physical graph currently plans and validates:

- source acquisition;
- transforms and placement-dependent work;
- effect intermediates;
- canvas composition;
- nested canvases;
- route transitions;
- output passes;
- rendered-output fan-out;
- encoded-output dispatch.

Production Vulkan submission requires a validated physical plan. Missing plans fail before native import.

Recent implementation binds Vulkan external-texture imports to declared physical source-acquisition operations and binds encoded delivery to declared physical encoded-dispatch operations. Physical identity is carried by typed fields, not parsed from operation-key strings.

Remaining roadmap work is to make the graph the sole physical authority for every temporary/effect resource and sustain that ownership under long-running in-flight pressure.

## Lifetime contracts

- Submission cleanup is `WaitForCompletionAsync(timeout, cancellationToken)` followed by `DisposeCompleted()`.
- `IRenderFrameSubmission` is not disposable; ownership is released only after completion rules pass.
- Command resources retain every referenced source lease, texture, framebuffer, descriptor set, temporary target, snapshot, fence, and output surface until completion.
- Fence timeout preserves potentially in-flight resources for retry or terminal failed ownership.
- Native handles are not logical texture identity.
- Providers transfer ownership once to `SourceRuntimeManager`.
- Provider unregister and engine shutdown await physical cleanup.
- Intermediate targets cannot be borrowed by concurrent in-flight submissions.
- GPU, keyed-mutex, provider, sink, encoder, route, and shutdown waits require explicit timeouts.
- Finalization errors remain observable.
- Sustained tests assert bounded high-water behavior and return to baseline after work finishes.

## Capability truth

`IHardwareMediaCapabilityProbe.ProbeAsync` is asynchronous and must not block the UI thread.

Capability snapshots are immutable and cached by adapter/device generation.

Windows Vulkan and D3D11 interop matches adapters by LUID. Missing identity or cross-GPU mismatch fails closed.

`Supported` or `Experimental` requires a real implementation plus the applicable evidence chain. Prototype, skeleton, contract-only, fake, nominal-hardware, and skipped-test results do not promote capabilities.

Product packet paths require trusted backend-output evidence.

Current hardware-media validation:

```powershell
./scripts/verify-engine-readiness-v14.ps1
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Hosted or portable CI cannot qualify physical GPU/capture/codec capabilities by itself.

## Windows physical path

```text
D3D11 capture/decode texture
  <-> NT shared handle / keyed synchronization
  <-> Vulkan external-memory image
  -> composition
  -> Vulkan output surface
  -> D3D11/NV12 conversion
  -> Media Foundation hardware H.264
  -> MP4 and/or RTMP packets
```

Implemented Windows paths include:

- Desktop Duplication;
- Windows Graphics Capture for HWND;
- Media Foundation webcam capture;
- PNG/JPEG static image upload;
- Media Foundation MP4 hardware decode accepting GPU samples only;
- Vulkan/D3D11 interop;
- Media Foundation hardware H.264;
- packet-only MP4;
- TCP RTMP/FLV.

They remain proof-gated and hardware-dependent until matching reports pass on the active adapter/driver.

## Studio boundary

Studio has explicit Design/Test and Runtime composition.

- Production saves canonical `MediaForgeProject` JSON.
- `StudioProjectSession` applies UI edits to canonical clones and commits session state only after successful atomic replacement.
- Valid fields not represented by the current UI are preserved.
- Draft and Live use engine sessions.
- Output cards use real route services where capability permits.
- Production runtime failure must not fall back to fake/design services.
- Avalonia overlays remain separate from native preview.
- Hosted preview uses a portable lifecycle contract and platform presenter.
- Runtime credentials never enter project JSON.
- Project replacement awaits physical draft disposal before session maps are cleared.

The active API/Studio integration checkpoint is defined by
[`MVP_API_STUDIO.md`](MVP_API_STUDIO.md).

## Portable audio boundary

`WTK.MediaForge.Audio` references portable contracts only.

Current portable implementation includes:

- serializable graph model;
- immutable compiled plans;
- pooled planar float32 blocks at 48 kHz;
- generated tone and silence;
- gain, mute, pan, polarity, mix, meter, and fixed delay;
- deterministic source/node DAG execution into buses;
- bounded Program Mix route fan-out;
- route-local drops on queue or pool pressure;
- timestamps, clocks, latency, drift, resampling, and A/V mapping contracts.

The real-time callback does not block, await, allocate, take contended locks, access disk, format logs, rebuild graphs, invoke UI, or call slow sinks.

Physical capture, loopback, application capture, playback, encode, mux, and Remote Scene audio remain unavailable until platform adapters and product proofs exist.

## Remote Scene boundary

Remote Scene media target:

```text
GPU surface
  -> existing hardware H.264 encoder
  -> WebRTC/SRTP
  -> encoded access units
  -> existing hardware decoder
  -> GPU surface
```

Current implementation includes:

- platform-neutral contracts;
- explicit encoded-packet leases;
- bounded publish/subscribe queues;
- reorder/jitter policy;
- hardware decode pump contract;
- telemetry and keyframe feedback;
- signaling service with authenticated bounded SDP/ICE relay;
- coturn credential integration;
- native ABI v2 contract and pinned supply-chain metadata.

The checked-in native backend is contract-test-only and deliberately unavailable. Publish/subscribe remains unavailable until the functional pinned adapter and Direct/TURN physical proofs pass.

Signaling never transports media and is not media capability evidence.

## Platform boundaries

- Windows: `net8.0-windows10.0.19041.0`, D3D11/DXGI, Win32, WGC, D3D11VA, Media Foundation, Vulkan interop.
- Linux planned: VAAPI, DRM PRIME/DMABUF, Vulkan import/export, Vulkan Video, or approved vendor interop.
- macOS planned: VideoToolbox, CVPixelBuffer/IOSurface, Metal/Vulkan bridge.

Unsupported platform adapters report a concrete unavailable reason. Portable projects never reference platform implementations.

## Current execution priority

1. Keep documentation and public contracts aligned with source reality.
2. Complete Physical RenderGraph authority.
3. Promote hosted native preview.
4. Complete and qualify the public API vertical.
5. Complete and qualify the Studio vertical.
6. Build physical audio adapters.
7. Build Linux physical media adapters.
8. Build Remote Scene media and later deferred features.

See `docs/ROADMAP_CURRENT.md` for exit criteria and `docs/MVP_API_STUDIO.md`
for the functional API/Studio integration scope.

## Documentation authority

Normative sources:

- `docs/ROADMAP_CURRENT.md`
- this file
- `docs/PRODUCT_MODEL.md`
- `docs/PUBLIC_API.md`
- `ARCHITECTURE.md`
- `docs/GPU_MEDIA_SUPPORT_MATRIX.md`
- `docs/AUDIO_SUPPORT_MATRIX.md`
- `docs/REVIEW_CHECKLIST.md`
- `docs/BUILD_AND_RELEASE.md`
- `AGENTS.md`

Files under `docs/history` are non-normative.
Historical readiness scripts live under `docs/history/readiness-scripts` and are
not current gates.
