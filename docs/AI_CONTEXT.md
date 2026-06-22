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
- `RenderOutputSinkDispatcher` fans out frames through bounded per-sink queues and keeps sink callbacks off the render thread.
- Source acquisition is routed through `SourceRuntimeManager` and `MediaSourceRuntime`; the engine must not manage a raw provider list as its runtime source model.
- Source buffers and sink queues are lease/reference infrastructure only. They must not copy pixels to CPU memory by default or become visual composition objects.
- `SourceFrameBuffer` render acquisition preserves the latest frame for `KeepLatest`, `Static`, and current `TimelineDriven` modes; only `Queue` consumes frames in order. Runtime cleanup must release the cached latest frame.
- Source frame acquisition failures are observable diagnostics and snapshot diagnostics. A failed or empty source must not crash the renderer or render pump.
- `RenderOutputSinkQueue` owns per-sink bounded backpressure policy. Slow sinks must release dropped leases and must not block the render thread.
- Runtime, snapshot, render-thread, backend, source-provider, output-sink, GPU lease, D3D11 physical slot-ring, and Vulkan implementation types are internal details.
- GPU wait APIs must use explicit timeouts.
- `MediaForgeEngine.ApplyProjectUpdateAsync`, `BindOutputAsync`, and `UnbindOutputAsync` are transactional. Failed updates/binds must preserve the previous public engine state.
- `MediaForgeEngine.StopAsync` must not dispose the backend if the render thread is still alive after dispose timeout. It reports `engine.backend_dispose_skipped_render_thread_alive` as fatal and leaves a controlled leak instead of risking use-after-free.
- CP1 visual correctness is proven by Vulkan offscreen pixel readback tests for center pixel, Fit transparency, Fill, Stretch, opacity, output letterbox color, canvas background, transparent layer over background, and clipped/fully outside layer geometry.
- CP1 descriptor capacity is explicitly sized for larger submits, and `VulkanExternalTextureRegistry` waiters use timeout diagnostics instead of indefinite blocking.

## Remaining Blockers

The application shell must not re-enable capture preview until it is wired through the hardened runtime path. The old direct `DesktopDuplicationCaptureSource -> VulkanPreviewRenderer` path must not return.

PAPI and the first runtime/sink foundation are complete. CP2 multi-layer, CP3 nested canvas, chroma/effects, productive preview, and real source/output integrations remain blocked until the roadmap explicitly starts the next renderer track.

## Review Style Expected

Act as project manager and senior technical lead:

- decisive instructions
- no product shortcuts
- no unresolved architecture choices left open
- tests and acceptance criteria for every lifetime change
