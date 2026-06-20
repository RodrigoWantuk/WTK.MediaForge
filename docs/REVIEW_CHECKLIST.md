# Review Checklist

Before considering a change complete, verify:

## General

- [ ] No new UI, NDI, encoder, audio, offscreen, or visual pipeline work before required gates.
- [ ] No new TODOs in lifetime paths.
- [ ] No swallowed exceptions in physical resource cleanup.
- [ ] Tests added for every lifetime change.
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

## Registry

- [ ] `VulkanExternalTextureRegistry` is internal.
- [ ] `VulkanExternalTextureRegistry.Acquire` does not call Vulkan import creation inside `lock (_gate)`.
- [ ] Unpublished imports are disposed when publish fails because the registry was disposed or the entry was removed.
- [ ] Failed import creation does not leave stuck entries.
- [ ] Acquire can retry cleanly after import creation failure.
- [ ] Concurrent acquire of the same texture creates one import.
- [ ] Concurrent acquire of different textures can create independently.

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
