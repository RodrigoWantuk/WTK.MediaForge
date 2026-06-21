# Review Checklist

Before considering a change complete, verify:

## General

- [ ] No new UI, NDI, encoder, audio, offscreen, or visual pipeline work before required gates.
- [ ] No new TODOs in lifetime paths.
- [ ] No swallowed exceptions in physical resource cleanup.
- [ ] Tests added for every lifetime change.
- [ ] Runtime/GPU/snapshot internals are not exposed as public product API.
- [ ] Public API changes update `docs/PUBLIC_API.md` and `Public_api_matches_approved_allowlist`.
- [ ] CP2, nested canvas, preview, NDI, encoder, webcam, RTSP, MP4, streaming, or audio work starts only after the roadmap explicitly opens that track.
- [ ] `dotnet test` passes.
- [ ] `scripts/test.ps1 -Tier Fast` passes.
- [ ] `scripts/test.ps1 -Tier Gpu` passes when touching GPU code.

## Submission

- [ ] No `DisposeAsync` or `IDisposable.Dispose` cleanup exists on submission types.
- [ ] Cleanup uses `WaitForCompletionAsync(timeout, ct)` then `DisposeCompleted()`.
- [ ] No hidden GPU wait without timeout.

## Backend

- [ ] `IRenderBackend` has no synchronous `WaitIdle()`.
- [ ] Runtime shutdown uses `WaitIdleAsync(timeout, ct)`.

## Vulkan Renderer

- [ ] `MediaForgeVulkanRenderer` is internal.
- [ ] Public creation is through factory.
- [ ] No `Simulate*` properties exist.
- [ ] Fault injection uses `IVulkanRendererFaultInjector`.
- [ ] `Dispose` does not mark the renderer disposed when active texture leases exist.
- [ ] Terminal `Dispose` attempts target, registry, and device cleanup and aggregates failures.
- [ ] CP1 framebuffers are not destroyed until the submitted command buffer fence completes and `DisposeCompleted()` runs.
- [ ] CP1 descriptor sets allocated per command buffer are freed after the fence, not immediately and not only at renderer dispose.
- [ ] CP1 offscreen target references are retained by submission resources and released after the fence.
- [ ] CP1 Fit produces transparent pixels outside fitted content, not clamped edge pixels.
- [ ] CP1 output letterbox and opacity have pixel-readback coverage.
- [ ] CP1 descriptor pool capacity is explicit and covered by many-layer submit tests.
- [ ] CP1 canvas render pass clears with `canvas.BackgroundColor`.
- [ ] CP1 layer scissors are clipped to framebuffer/canvas bounds and fully outside layers are skipped.

## Registry

- [ ] `VulkanExternalTextureRegistry` is internal.
- [ ] `VulkanExternalTextureRegistry.Acquire` does not call Vulkan import creation inside `lock (_gate)`.
- [ ] Unpublished imports are disposed when publish fails because the registry was disposed or the entry was removed.
- [ ] Failed import creation does not leave stuck entries.
- [ ] Acquire can retry cleanly after import creation failure.
- [ ] Concurrent acquire of the same texture creates one import.
- [ ] Concurrent acquire of different textures can create independently.
- [ ] Waiters on in-flight import creation use timeout diagnostics and do not block indefinitely.

## Engine

- [ ] `ApplyProjectUpdateAsync` edits a clone and swaps only after validation.
- [ ] Invalid project updates preserve `CurrentProject`, project snapshots, and frame publication.
- [ ] `CurrentProject` returns a clone/snapshot, never the engine-owned mutable project instance.
- [ ] `StartAsync` requires a loaded project.
- [ ] `StopAsync` returns to `Loaded` when the project remains loaded.
- [ ] `StartTimeout`, `CommandTimeout`, and `StopTimeout` are applied to public long-running operations.
- [ ] The render pump stops before render-thread shutdown.
- [ ] The render pump reports frame drops when the render thread is backpressured.
- [ ] `BindOutputAsync` creates the new sink/binding before replacing the old sink.
- [ ] Bind failures dispose the failed new sink and keep the old sink registered.
- [ ] `UnbindOutputAsync` enqueues unbind before sink disposal.
- [ ] `StopAsync` skips backend dispose when render thread is still alive and reports a fatal diagnostic.
- [ ] Backend dispose still runs when render thread cleanup failed after the thread stopped.
- [ ] Render commands used by public engine APIs are acknowledged before the public call returns success.
- [ ] Public engine failures use typed public exceptions.
- [ ] Engine diagnostics/state/frame-drop events are raised without allowing event-handler failures to kill the render thread.

## Render Output Sinks

- [ ] Public output consumption uses `RenderOutput -> RenderOutputSink(s)`, not direct canvas-to-preview/encoder/NDI paths.
- [ ] Public sink APIs do not expose Vulkan images, D3D11 textures, raw shared handles, render-thread types, snapshots, or GPU frame slots.
- [ ] Sink callbacks run outside the render thread.
- [ ] Each sink has bounded queue/backpressure behavior.
- [ ] Sink attach failure rolls back without leaving a partially registered sink or surface.
- [ ] Sink detach stops delivery, stops the sink, disposes it, and removes automatic surface bindings when no sinks remain.
- [ ] One `RenderOutput` can feed multiple sinks without rendering the same output more than once per frame.

## Texture Identity

- [ ] Dedupe uses `VulkanExternalTextureKey`.
- [ ] No dedupe by `nint` or `DangerousGetHandleForInterop`.

## Provider Lifecycle

- [ ] `StartAsync`, `StopAsync`, `DisposeAsync`, and `Dispose` use one lifecycle gate.
- [ ] `DisposeFailed` exists.
- [ ] Non-timeout failure does not leave `Disposing`.
- [ ] Retry after `DisposeTimedOut` or `DisposeFailed` works when the failed resource is recoverable.

## D3D11 Ring

- [ ] Dispose attempts all slots.
- [ ] Any physical dispose failure faults `FullyDisposed`.
- [ ] Manager moves faulted resources to failed state.
