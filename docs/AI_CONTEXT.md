# AI Context - WTK MediaForge

## Product Goal

WTK MediaForge is a Windows/.NET GPU-first media compositor/capture/render engine.

The product direction is:

- GPU-first capture and composition
- avoid raw frame copies through RAM
- D3D11 Desktop Duplication capture
- Vulkan rendering
- D3D11 shared texture / NT shared handle / keyed mutex interop
- strict lifetime ownership
- predictable shutdown
- production-quality API contracts

## Current Baseline

The hardened runtime path is:

```text
DesktopDuplicationFrameProvider
  -> GpuFrameLease / D3D11SharedTextureFrameHandle
  -> RenderFrameSnapshot
  -> MediaForgeRenderThread
  -> PendingRenderSubmissionTracker
  -> MediaForgeVulkanRenderer via MediaForgeVulkanRenderBackendFactory
```

The legacy WinForms preview path has been removed as a product path because it used native handle identity, unbounded GPU waits, and CPU readback diagnostics in the frame loop.

## Final Contracts

- Submission cleanup is always `WaitForCompletionAsync(timeout, cancellationToken)` then `DisposeCompleted()`.
- `IRenderFrameSubmission` does not inherit or implement disposable cleanup APIs.
- `IRenderBackend` exposes only `WaitIdleAsync(timeout, cancellationToken)`.
- `MediaForgeVulkanRenderer` is internal and created through `MediaForgeVulkanRenderBackendFactory`.
- `MediaForgeVulkanRenderer.Dispose` preflights active registry leases before marking the renderer disposed. Once terminal dispose starts, it attempts target, registry, and device cleanup and aggregates cleanup failures.
- Renderer fault testing uses `IVulkanRendererFaultInjector`.
- External texture identity is `VulkanExternalTextureKey = GpuTextureId + Width + Height + Format`.
- Provider lifecycle is serialized through one `_lifecycleGate`.
- D3D11 ring physical dispose failure faults `FullyDisposed`.
- Vulkan registry import creation occurs outside the global registry lock.
- `VulkanExternalTextureRegistry` is internal. Imports created but not published because of dispose/removal races must be disposed before the failure is rethrown.
- CP1 Vulkan command-buffer resources are retained by `VulkanSubmissionResourceScope` and released only from `VulkanRenderFrameSubmission.DisposeCompleted()` after the fence completes. This includes framebuffers, descriptor sets, and offscreen target references.
- Public product API boundaries are defined in `docs/PUBLIC_API.md` and enforced by `Public_api_matches_approved_allowlist`.
- `MediaForgeWindows.CreateEngine` is the public Windows entrypoint. The public parameterless `MediaForgeEngine` constructor was removed.
- `MediaForgeProjectBuilder` is the happy-path public authoring API for desktop-to-canvas-to-offscreen projects.
- `MediaForgeEngine.CurrentProject` returns a deep clone. Engine-owned mutable project state is private and can only be changed through `LoadProjectAsync` or `ApplyProjectUpdateAsync`.
- `StartAsync` requires a loaded project. `StopAsync` returns to `Loaded` when the project remains loaded.
- `MediaForgeWindows.CreateEngine` applies start, command, stop, and render pump frame-rate options.
- `MediaForgeRenderPump` publishes frames continuously while running and reports backpressure drops instead of flooding the render thread.
- Public output consumption is `RenderOutput -> RenderOutputSink(s)`. The internal surface remains GPU/backend-owned; public sinks receive leases and metadata without exposing Vulkan/D3D11 handles.
- Public `Scene` terminology is an ergonomic alias over `MediaForgeCanvas`. Do not introduce a second scene primitive unless the product model is explicitly revised.
- Scene editing semantics are engine-owned. `BeginSceneEditSessionAsync`,
  `ApplySceneMutationAsync`, `ApplySceneDraftAsync`, and
  `DiscardSceneDraftAsync` are the public contract for Studio/hosts. `Live`
  sessions mutate published scene state after validation and normal sinks see
  the next frame. `Apply` sessions mutate an isolated draft; published sinks
  continue using the published version until commit.
- Each runtime canvas has a `SceneVersionId`. `CanvasDrawObject` carries a
  `SceneVersionBinding` (`Published`, `Draft`, or `ExplicitVersion`) so nested
  scenes, draft previews, cache keys, and apply transitions can render the
  correct version graph. `MediaForgeRenderOutput.SceneVersionBinding` lets an
  output route target a published, draft, or explicit scene version; normal
  output routes use published binding.
- Scene dependency graph/planner identifies direct consumers, transitive
  consumers, and affected output routes when a scene draft is applied. Apply
  transition policy is reported at commit time. The engine captures old/new
  `SceneVersionGraph` state for each affected output route, retains the
  pre-commit project snapshot while the route transition is active, and
  `RenderFrameSnapshotFactory` exposes `PreviousCanvasId` plus progress for the
  compositor. Transition frames now compile from `RenderFrameSnapshot` into a
  physical graph with explicit old-canvas, current-canvas, output-transition,
  and output-pass operations. Vulkan output composition consumes those physical
  output-pass dependencies and caches physical canvas targets per submission so
  multiple outputs can reuse the same rendered canvas target. Vulkan blur
  source-layer intermediates execute as physical, placement-aware
  `RenderEffectIntermediate` prepasses and carry canvas/source/draw-object
  metadata. Source acquisition, remaining effect intermediates, full
  canvas-pass ownership, and encoded routes still need to move behind physical
  pass execution.
- The public authoring foundation includes typed source/output helper factories, `Scene(...)`, route helpers, and package export/import APIs.
- Multiple canvases/scenes can be routed independently to outputs and sinks. The same source can feed multiple scenes/layers, and the renderer must minimize redundant GPU work.
- The render graph target is `Outputs/Sinks -> RenderOutput -> Canvas/Scene -> DrawObjects -> Sources -> Effects`. The current internal planner deduplicates source frame, reusable source effect-chain, primitive layer, canvas render, output-transition, and output pass nodes by stable keys.
- Sinks never cause rendering directly. They consume completed `RenderOutput` frame leases after the renderer has produced the surface.
- Package JSON is product model data only. It may contain schema versions, ids, type ids, typed settings, transforms, effects, canvas graph, routes, and metadata. It must not contain runtime leases, native handles, Vulkan/D3D11 objects, command buffers, fences, backend worker state, sink queues, or secrets unless explicitly exported.
- Scene package import must build and validate a candidate project first. Replace, merge-as-new-scene, merge-presets-only, and dry-run modes must not mutate the existing engine/project state on failure.
- `RenderedOutputFrame` carries an internal `IRenderedOutputSurfaceLease`; public `RenderOutputFrameLease` hides backend details while preserving lifetime for real rendered surfaces.
- `RenderOutputSinkDispatcher` fans out frames through bounded per-sink queues and keeps sink callbacks off the render thread.
- `RenderOutputSinkDispatcher` uses explicit sink stop timeouts for detach/dispose and reports hung sinks instead of blocking engine shutdown indefinitely.
- `RenderOutputSinkQueue` returns explicit enqueue outcomes and never disposes leases. The dispatcher owns release of undelivered or backpressured frames.
- `CpuReadbackSink` is the first public visual output sink for tests, debug, samples, and validation. It must not become the primary preview, encoder, or streaming path.
- Vulkan offscreen output targets are replaced before a submit when the current target still has submission or sink references, so a slow sink cannot read pixels overwritten by the next frame.
- Source acquisition is routed through `SourceRuntimeManager` and `MediaSourceRuntime`; the engine must not manage a raw provider list as its runtime source model.
- Source buffers and sink queues are lease/reference infrastructure only. They must not copy pixels to CPU memory by default or become visual composition objects.
- `SourceFrameBuffer` render acquisition preserves the latest frame for `KeepLatest`, `Static`, and current `TimelineDriven` modes; only `Queue` consumes frames in order. Runtime cleanup must release the cached latest frame.
- Source frame acquisition failures are observable diagnostics and snapshot diagnostics. A failed or empty source must not crash the renderer or render pump.
- `RenderOutputSinkQueue` owns per-sink bounded backpressure policy. Slow sinks must release dropped leases and must not block the render thread.
- `RenderOutputSinkQueue` returns explicit enqueue results so workers are signaled only when a queue gains a pending frame; replacing a pending frame does not over-release the semaphore.
- Runtime, snapshot, render-thread, backend, source-provider, output-sink, GPU lease, D3D11 physical slot-ring, and Vulkan implementation types are internal details.
- GPU wait APIs must use explicit timeouts.
- `MediaForgeEngine.ApplyProjectUpdateAsync`, `BindOutputAsync`, and `UnbindOutputAsync` are transactional. Failed updates/binds must preserve the previous public engine state.
- `MediaForgeEngine.StopAsync` must not dispose the backend if the render thread is still alive after dispose timeout. It reports `engine.backend_dispose_skipped_render_thread_alive` as fatal and leaves a controlled leak instead of risking use-after-free.
- `MediaForgeEngine.StopAsync` attempts runtime cleanup even when the engine is already `Failed`; it no longer returns silently while runtime resources remain alive.
- CP1 visual correctness is proven by Vulkan offscreen pixel readback tests for center pixel, Fit transparency, Fill, Stretch, opacity, output letterbox color, canvas background, transparent layer over background, and clipped/fully outside layer geometry.
- CP1 descriptor capacity is explicitly sized for larger submits, and
  `VulkanExternalTextureRegistry` waiters use explicit timeout diagnostics,
  fail future waiters fast after a timed-out import, unwrap import failures
  without silent retry loops, and avoid async-over-sync waits in the registry
  import waiter path.
- `DesktopDuplicationFrameProvider` reconnect replaces the D3D11 slot ring when
  a new duplication session/device is created, retires the old ring through
  `RetiredGpuResourceManager`, and marks the provider `Failed` if reconnect
  cannot restore a valid GPU session. Reconnect cleanup must stop and dispose
  superseded or failed duplication sessions; cleanup failures are diagnostic and
  prevent reconnect from being reported as successful.
- CP3 solid layer rendering is implemented in Vulkan with transform, clipping, opacity, normal alpha blending, and pixel tests.
- CP3 nested canvas rendering is implemented in Vulkan by rendering child canvases into submission-retained intermediate targets and compositing them into parent canvases with transform, opacity, and depth-8 coverage.
- CP3 `ChromaKeyEffect` is the only supported source-layer effect. Unsupported/invalid/multiple chroma configurations emit explicit diagnostics and are covered by `Cp3ChromaKeyEffectTests`.
- Vulkan offscreen composition is implemented through `VulkanCompositionShaderPipelines` and `VulkanOffscreenCompositor`.
- `PreviewPanelSink` presents completed Vulkan offscreen surfaces to a Win32 panel through an internal swapchain blit. Stop/dispose waits for in-flight presentation to become idle before removing the panel presenter; cancellation/timeout preserves the presenter instead of risking use-after-free. It is the GPU preview path; `CpuReadbackSink` remains debug/sample only.
- Sink attach timeout is owned by `RenderOutputSinkDispatcher`; the engine does not wrap that operation in a competing timeout that could abandon dispatcher cleanup before the sink observes cancellation.
- Source/output type catalogs now include product contracts for animated images, Lottie, IP camera, encoded file, SRT, RTSP, and HLS. These are project/API contracts only until runtime adapters land.
- Window capture remains a project/API contract only. Capability reports keep it
  `Planned` until a Windows Graphics Capture provider publishes D3D11 GPU frame
  leases; it must not be shown as available. The Windows engine recognizes the
  source contract and fails with a typed unavailable-feature diagnostic instead
  of a generic missing-provider error.
- Windows webcam input has a real Media Foundation provider. It enumerates video
  capture devices, selects a supported native format, converts NV12/YUY2/RGB32
  at the OS boundary, immediately uploads into a D3D11 shared GPU texture, and
  publishes bounded `KeepLatest` GPU leases. Capability reports promote webcam
  only when `proof.media_io.webcam_input.product` passes on the current machine;
  missing hardware, permission, or driver support remains `Unavailable` with a
  reason.
- Windows PNG/JPEG static image sources decode once into `StaticCpuAsset`, upload to a D3D11 shared texture, release the CPU pixel copy, and then publish GPU frame leases. WebP remains Planned until decoder/license review.
- Decode/video resource ownership remains split by purpose: `GpuTextureLease`
  is the internal pooled GPU texture lease, `GpuFrameLease` is the render
  source lease consumed by snapshots/renderers, and hardware encoder leases
  remain encoder-specific. `DecodedFrameToSourceFrameAdapter` is the internal
  bridge from `DecodedGpuFrame` to `GpuFrameLease`; it preserves source id,
  PTS, frame number, size, and releases the original texture lease when the
  render source lease is released.
- `WindowsVideoFileSourceProviderFactory` is registered in the normal Windows
  provider chain and uses the product hardware decoder by default. If
  Media Foundation/D3D11VA cannot produce GPU-backed decoded frames, the source
  fails observably; it must not fall back to placeholder decode or system-memory
  frame transport. Capability reports promote MP4 video input only when
  hardware decode, decode-to-render, and MP4 input product proofs pass together.
- `MediaFoundationHardwareVideoEncoder` has a separate product session
  boundary (`MediaFoundationHardwareH264EncoderSession`) from the canned
  prototype bridge. The product path is configured by
  `HardwareVideoEncoderSettings`, uses a shared `MediaFoundationRuntime` lease,
  and may be validated on a hardware machine by the Windows H.264 proof runner.
  Only internal tests may opt into `PrototypeMediaFoundationH264EncoderSession`,
  and its audit evidence must remain `Prototype`.
- Encoder input export now requires pixel-format compatibility. If an encoder
  requires NV12 and the renderer produced BGRA/RGBA, the path must go through an
  `IHardwareEncoderFormatConverter`; `D3D11BgraToNv12Converter` uses the D3D11
  VideoProcessor path when a D3D11 shared texture source/device is available
  and records `GpuFormatConversionSucceeded` only after a backend blit succeeds.
  Unsupported devices/sources record `GpuFormatConversionUnavailable`; CPU
  readback or staging remains prohibited.
- Recording and streaming outputs are packet-sink responsibilities, not render
  sink responsibilities. `IEncodedPacketSink`, `RecordingMp4PacketSink`, and
  `RtmpPacketSink` consume `EncodedVideoPacket`; `IRenderOutputSink` consumers
  receive rendered surfaces only.
- Encoded packet fanout runs through `EncodedOutputRouter` with explicit
  per-consumer backpressure policy and write timeout. Recording consumers use
  bounded backpressure; network consumers fail the affected output path instead
  of blocking render or encode threads indefinitely.
- Encoded outputs are registered through an explicit route factory boundary.
  `MediaPipelineRuntime` owns render-output-to-encode routes, applies
  per-output backpressure policy, and exposes high-level
  `EncodedOutputRuntimeSnapshot` state/counters for API and Studio use without
  exposing encoder workers or backend surfaces. Recording policies do not use
  `KeepLatest`; queue exhaustion becomes observable failure/backpressure, not
  a silent frame drop.
- `CapabilityProofAggregator` resolves MP4 recording, RTMP streaming, and MP4
  video-file input capability from required hardware media proofs. The Windows
  capability report also promotes the webcam source entry when the webcam
  product proof passes. MP4 recording requires hardware encode,
  render-to-encode, MP4 recording, and MP4 output product proofs. MP4 video
  input requires hardware decode, decode-to-render, and MP4 input product
  proofs. Missing proof chains make these features `Unavailable` with explicit
  proof reasons until the required proof chain passes on the current machine.
- `EncodedVideoPacket` carries explicit codec, bitstream format, presentation
  time, optional duration, and optional codec configuration bytes. MP4/RTMP
  packet sinks must reject unknown H.264 bitstream format instead of accepting
  opaque bytes. Backend-output-validated packet evidence is observable but not
  publicly forgeable; it is created only by trusted implementation assemblies
  after a real backend produces output. Public MP4 and RTMP packet sinks reject
  prototype or contract-only packets; test-only prototype transports require an
  explicit opt-in. The prototype MP4 writer no longer fabricates SPS/PPS data.
- Rendered-output encoder input preparation is a separate GPU-only step:
  direct export is allowed only when the encoder requirement matches the
  rendered surface; otherwise a GPU converter must produce the requested
  encoder input format. Missing conversion records an unavailable audit event
  and fails instead of using CPU staging. Conversion must return a new GPU
  lease with the exact requested size, format, and transport kind.
- `MediaFoundationHardwareVideoDecoder` has a real product SourceReader session
  boundary (`MediaFoundationFileHardwareVideoDecoderSession`) separate from the
  placeholder prototype bridge. It uses a D3D11 device manager, requests NV12
  output, and accepts decoded frames only when Media Foundation returns an
  `IMFDXGIBuffer` GPU texture. That texture is copied on GPU into a D3D11
  shared texture lease. System-memory decoded samples and placeholder textures
  remain unavailable for product decode; only internal tests may opt into the
  prototype bridge, and its audit evidence must remain `Prototype`.
- Decode-to-render product proof is executable in v12: it generates a real MP4
  asset from the render-to-encode/MP4 proof path, decodes it through Media
  Foundation D3D11VA, adapts the decoded GPU frame to a source lease, and
  submits it through Vulkan. It still requires `BackendOutputValidated`
  evidence for hardware decode and fails as unavailable if the machine lacks
  the required hardware/driver/API/interoperability path. Prototype decoder
  frames cannot satisfy this proof.
- The logical `RenderGraphExecutor` now carries renderable `GpuFrameReference`
  resources through source/effect/canvas/output nodes and skips downstream work
  when a source frame is unavailable. The engine attaches the per-frame graph
  execution result to `RenderFrameSnapshot` after source leases are acquired,
  so backends can audit/consume the DAG for the submitted frame. Vulkan output
  composition resolves physical output and transition dependencies from this
  plan and reuses physical canvas targets across compatible output passes in
  the same submission. Blur is the first physical effect intermediate consumed
  by Vulkan; because the current blur implementation pre-renders the positioned
  layer into a canvas-sized target, blur nodes are placement-dependent and are
  keyed with canvas and draw-object metadata. The graph still does not own
  source acquisition, remaining effect intermediates, or the full canvas GPU
  pass lifecycle.
  A physical pass checkpoint now exposes source acquisition, effect
  intermediates, canvas passes, output passes, output transitions, and
  rendered-output fanout, but `GpuTextureLease` output resources are reserved
  for the future bridge where source/effect/canvas intermediate passes are
  fully owned by the physical executor.
- `ColorCorrectionEffect` is implemented in the Vulkan source-layer fragment
  shader for brightness, contrast, saturation, and hue. The shader applies
  source sample -> color correction -> chroma key -> opacity.
- `BlurEffect` is implemented for Vulkan source layers by rendering the source
  layer into a pooled intermediate target, running horizontal and vertical blur
  shader passes, then compositing the blurred target into the canvas with the
  layer opacity. Blur intermediates are now driven by physical graph
  `RenderEffectIntermediate` operations for offscreen composition. The current
  scope is product-validated by Vulkan pixel, physical intermediate, and
  intermediate-pool reuse tests.
- `TextDrawObject` includes `FontFamily`; snapshots propagate it into the Vulkan
  renderer. Text rendering uses a rasterized glyph atlas uploaded to a Vulkan
  sampled image. The Vulkan project owns atlas upload/rendering only. Font
  rasterization is supplied by OS-specific adapters, currently the Windows
  `System.Drawing` adapter in `WTK.MediaForge.Windows`; Linux/macOS must add
  their own adapters instead of adding platform dependencies to Vulkan.
  Current validation covers Windows Vulkan text layers and verifies that atlas
  content is keyed by text/family/size rather than by font size alone.
- `MediaForgeRenderOutput.RouteTransition` describes the route transition for
  an output. `OutputRouteTransitionRuntime` advances progress from explicit
  frame delta time only. Vulkan output composition supports cut and fade by
  rendering previous/current canvas targets and alpha-blending the current
  output pass over the previous one.

## GPU Media Transport Law (vNext)

- Uncompressed continuous video frames must stay in GPU/VRAM on the product path.
- Continuous video decode and encode must use platform hardware acceleration.
  If the GPU/hardware path is unavailable, the feature is `Unsupported`,
  `Planned`, or `Unavailable`; it must not fall back to software decode/encode.
- CPU/RAM carries encoded packets, static asset load buffers, metadata, and registered exceptions only.
- Formal types: `EncodedVideoPacket`, `GpuVideoFrame` (GPU lease), `StaticCpuImageAsset` (load-only), raw CPU video prohibited on product path.
- Registered raw CPU exceptions use `RawCpuVideoFrameException` with kinds: `PixelTestOnly`, `ManualScreenshotOnly`, `WebcamSystemRawInput`. Static image load is **not** an exception.
- `CpuReadbackSink` is debug/test only (`DebugOnlyCpuReadback`). Product sinks consume `GpuSurface` or `EncodedPacket` after hardware encode.
- FFmpeg is deferred until the native hardware MP4/RTMP product path is
  sustained and a separate encoded-packet/container-only legal review approves
  the scope.
- Platform media adapters stay in their own assemblies: Windows uses
  D3D11/D3D11VA/Media Foundation first; Linux targets VAAPI/DRM/DMABUF,
  Vulkan Video, or approved vendor interop in Linux-specific projects; macOS
  targets VideoToolbox/CVPixelBuffer/IOSurface/Metal in macOS-specific
  projects.
- NVIDIA, AMD/Radeon, Intel integrated/discrete, Apple, VAAPI, Vulkan Video,
  and future vendor SDK paths must be runtime-detected through capability
  reports. GPU vendor names alone must never imply support.
- Hardware media promotion is driven by composite proof chains: render-to-encode,
  hardware H.264 encode, MP4 recording packet output, hardware H.264 decode,
  decode-to-render, MP4 output, MP4 input, webcam input, RTMP network output,
  and NDI input/output. Each proof is represented by `HardwareMediaProof` and
  must be `Passed` before a product capability can advertise support.
- `MediaForgeWindows.CreateHardwareMediaProofRegistry()` registers Windows
  proof runners, including H.264 hardware encode, render-to-encode, MP4
  recording, MP4 output, RTMP output, hardware decode, decode-to-render, MP4
  input, webcam input, and NDI input/output product proofs.
  `GetCapabilityReportWithHardwareProofsAsync` is explicit because proof
  execution may touch D3D11/Media Foundation hardware and must not be hidden in
  cheap UI capability probes.
- The Windows NDI adapter detects an installed/loadable Standard NDI runtime
  dynamically (`NDI_RUNTIME_DIR_V6`, `NDI_RUNTIME_DIR_V5`, application
  directory, NuGet native assets, `Program Files` runtime folders, or `PATH`).
  Licensed runtime DLLs can be placed under `third_party/ndi/windows/*` and
  packed as `runtimes/win-*/native` assets. `MediaForgeWindows.FindNdiSourcesAsync`
  performs Standard SDK source discovery only. Runtime detection/discovery does
  not promote NDI video to product support; video input/output remain blocked
  until GPU-safe input/output proofs pass. The Standard SDK raw frame-buffer
  path is not accepted for continuous product video.
- Capability probing uses `IHardwareMediaCapabilityProbe.ProbeAsync`; Studio loads capabilities in background.
- `CapabilityEntry.ProductReadinessStatus` is separate from `MediaForgeSupportStatus`.
  `Prototype` and `Skeleton` readiness entries must never be emitted as
  `Supported` or `Experimental`.
- `./scripts/verify-engine-readiness-v12.ps1` is the current official
  product-boundary gate for engine media work. Older v9/v10/v11 scripts remain
  historical or layered evidence, but v12 is the entrypoint that should be used
  for new hardware-first media changes. `./scripts/verify-engine-readiness-v10.ps1`
  adds GPU and Performance tiers for full local readiness.
  `./scripts/verify-engine-readiness-v11.ps1` adds the encoded route/status,
  capability proof aggregation, Windows media proof truth checks, and an
  operational media proof report on top. The report is generated at
  `test-reports/media-proof-report.json` and
  `test-reports/media-proof-report.md` by
  `./scripts/generate-media-proof-report.ps1`.
  v12 runs the v11 baseline and adds checks for
  `EncodedVideoProfile`, Media Foundation encoder D3D11 device ownership, and
  refreshed proof-report generation. Proof runners may report `Unavailable`
  only for actual runtime hardware/driver/platform absence, not for pending
  implementation stubs.
  `-RequireHardwareMedia` additionally fails release validation unless all
  required v12 hardware media proofs pass on the target machine.
- `IMediaTransportAuditSink` records transport events; product paths must not emit `CpuReadbackAttempted` or `StagingBufferCreated`.

## GPU Resource Pool (Phase 2)

- All logical engine textures are acquired via `GpuResourcePool` and exposed as `GpuTextureLease`.
- Vulkan offscreen/intermediate targets use `VulkanGpuResourcePool`; physical wrappers implement `IGpuPhysicalResource` and retire through `RetiredGpuResourceManager`.
- Texture recycle honors pending `GpuFence` signals; physical destroy is deferred until pool retirement or renderer dispose.
- Native Vulkan/D3D11 handles must not leak into Composition or public API layers.

## Frame Scheduler (Phase 2 Commit 02)

- `FrameScheduler` owns frame pacing and produces `FrameExecutionContext` per tick.
- Flow: `FrameScheduler -> MediaForgeRenderThread -> IRenderBackend`; sinks consume completed output leases only.
- `MediaForgeRenderPump` is a compatibility wrapper over `FrameScheduler`.
- `FrameExecutionContext` carries `FrameId`, `Timestamp`, `FrameBudget`, `TargetOutputs`, and `SynchronizationPrimitives`.

## Studio UI Direction

WTK MediaForge Studio is the planned Avalonia desktop application for users who want a complete tool instead of direct API usage. The approved UI direction is documented in:

- `docs/UI_STUDIO_DESIGN.md`
- `docs/UI_REACT_TO_AVALONIA_MAPPING.md`
- `docs/UI_IMPLEMENTATION_PLAN.md`
- `docs/UI_ACCEPTANCE_CHECKLIST.md`
- `docs/STUDIO_UI_RECOVERY_PLAN.md`
- `docs/STUDIO_UI_VISUAL_ACCEPTANCE.md`

The Studio shell must be built with Avalonia UI, C#, MVVM, CommunityToolkit.Mvvm, compiled bindings, centralized dark theme resources, and mock/design data for the first milestone.

Current Studio state:

- `WTK.MediaForge.Studio` contains the first Avalonia mock shell.
- Studio ViewModel tests are included in `./scripts/test.ps1 -Tier Fast`.
- The current UI milestone is Studio UI product reset v0.2: shared mock
  document state, native vector icons, left-side scenes-only navigation,
  source library dialog, scene-scoped editable canvas, right-side
  Producao/Saidas cards, explicit scene-to-output routing with transitions,
  contextual Propriedades panel, bottom Camadas/Saidas da cena workbench,
  localization foundation, and ViewModel/viewport coverage.
- Fake Studio services own mock project, output, diagnostics, selection, and
  contextual properties behavior. Engine service types may exist internally for
  future bridge work, but the main UI must not expose Start/Stop Engine.
- `StudioSelectionState` is the single selection contract for explorer/layer/canvas/output selection.
- Output UI states are typed enums, not only booleans.
- The internal Studio header is an app header; native OS chrome remains active for now.

The downloaded React/Lovable prototype is a visual/component-behavior reference only. Do not embed React, Tailwind, WebView, Vite, Electron, or browser runtime dependencies into the Studio app. Translate the prototype into Avalonia controls, styles, resources, view models, commands, and data templates.

Allowed in the UI track before runtime gates open:

- Avalonia shell layout;
- centralized dark theme;
- mock scenes-only navigation, source library dialog, canvas editor,
  production output cards, contextual properties panel, bottom
  Camadas/Saidas da cena workbench, explicit output routing with transitions,
  and advanced diagnostics/performance placeholders outside the main workbench;
- fake Stream/Record UI state driven by configured outputs, not by a visible
  engine toggle;
- fake Studio service boundaries and unified selection state;
- Studio-only mock document state and editable mock preview overlays;
- ViewModel tests for selection, command state, inspector resolution, and mock status.

Not allowed in the UI track before runtime gates open:

- real webcam/desktop/media/NDI/RTSP adapters;
- real recording/streaming/virtual-camera/NDI outputs;
- real audio capture/mix/mux/equalization;
- real native/GPU preview integration before the `PreviewPanelSink` reliability milestone;
- resurrecting old direct preview/capture paths.

The UI must hide internal GPU/backend details. Users may see health/status concepts such as backend, FPS, frame time, dropped frames, output health, source buffering, and warnings, but must not manipulate native handles, leases, fences, keyed mutexes, command buffers, or backend-owned surfaces.

## Remaining Blockers

The application shell must not re-enable capture preview until it is wired through the hardened runtime path. The old direct `DesktopDuplicationCaptureSource -> VulkanPreviewRenderer` path must not return.

PAPI, CP2 multi-layer, CP3 solid/nested/chroma, first public visual sink, scene
routing helpers, package serialization foundation, and render-graph planning
foundation are complete. Productive preview, additional effects, real media
source adapters, encoder/streaming/NDI/virtual-camera sinks, runtime-connected
UI shells, plugin APIs, and audio remain blocked until the roadmap explicitly
starts those tracks. The Avalonia Studio mock shell is the only allowed UI
track before those gates open.

## Review Style Expected

Act as project manager and senior technical lead:

- decisive instructions
- no product shortcuts
- no unresolved architecture choices left open
- tests and acceptance criteria for every lifetime change
