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
- GPU wait APIs must use explicit timeouts.

## Remaining Blockers

The application shell must not re-enable capture preview until it is wired through the hardened runtime path. The old direct `DesktopDuplicationCaptureSource -> VulkanPreviewRenderer` path must not return.

Offscreen render target scaffolding exists, but real visual/offscreen composition must wait until the hardened preview binding is in place and tested.

## Review Style Expected

Act as project manager and senior technical lead:

- decisive instructions
- no product shortcuts
- no unresolved architecture choices left open
- tests and acceptance criteria for every lifetime change
