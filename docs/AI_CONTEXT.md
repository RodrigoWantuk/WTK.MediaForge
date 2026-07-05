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
- The public authoring foundation includes typed source/output helper factories, `Scene(...)`, route helpers, and package export/import APIs.
- Multiple canvases/scenes can be routed independently to outputs and sinks. The same source can feed multiple scenes/layers, and the renderer must minimize redundant GPU work.
- The render graph target is `Outputs/Sinks -> RenderOutput -> Canvas/Scene -> DrawObjects -> Sources -> Effects`. The current internal planner deduplicates source frame, reusable source effect-chain, canvas render, and output pass nodes by stable keys.
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
- CP1 descriptor capacity is explicitly sized for larger submits, and `VulkanExternalTextureRegistry` waiters use timeout diagnostics instead of indefinite blocking.
- CP3 solid layer rendering is implemented in Vulkan with transform, clipping, opacity, normal alpha blending, and pixel tests.
- CP3 nested canvas rendering is implemented in Vulkan by rendering child canvases into submission-retained intermediate targets and compositing them into parent canvases with transform, opacity, and depth-8 coverage.
- CP3 `ChromaKeyEffect` is the only supported source-layer effect. Unsupported/invalid/multiple chroma configurations emit explicit diagnostics and are covered by `Cp3ChromaKeyEffectTests`.
- Vulkan offscreen composition is implemented through `VulkanCompositionShaderPipelines` and `VulkanOffscreenCompositor`.
- `PreviewPanelSink` presents completed Vulkan offscreen surfaces to a Win32 panel through an internal swapchain blit. It is the GPU preview path; `CpuReadbackSink` remains debug/sample only.
- Sink attach timeout is owned by `RenderOutputSinkDispatcher`; the engine does not wrap that operation in a competing timeout that could abandon dispatcher cleanup before the sink observes cancellation.
- Source/output type catalogs now include product contracts for animated images, Lottie, IP camera, encoded file, SRT, RTSP, and HLS. These are project/API contracts only until runtime adapters land.


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
- The current UI milestone is Studio UI recovery/productization: shared mock
  document state, native vector icons, searchable Project Explorer, editable
  preview canvas, rich inspectors, bottom workbench polish, localization
  foundation, and ViewModel coverage.
- Fake Studio services own mock project, engine, output, diagnostics, selection, and inspector behavior.
- `StudioSelectionState` is the single selection contract for explorer/layer/canvas/output selection.
- Engine/output UI states are typed enums, not only booleans.
- The internal Studio header is an app header; native OS chrome remains active for now.

The downloaded React/Lovable prototype is a visual/component-behavior reference only. Do not embed React, Tailwind, WebView, Vite, Electron, or browser runtime dependencies into the Studio app. Translate the prototype into Avalonia controls, styles, resources, view models, commands, and data templates.

Allowed in the UI track before runtime gates open:

- Avalonia shell layout;
- centralized dark theme;
- mock Project Explorer, canvas, inspector, bottom workbench, output monitor, diagnostics, performance, and future audio placeholder;
- fake Start/Stop/Stream/Record UI state;
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
