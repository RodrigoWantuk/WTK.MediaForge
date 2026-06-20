# AGENTS.md

## Project Role

You are working on WTK MediaForge, a GPU-first media compositor/capture/render engine.

Act as a senior technical implementer. Follow the current roadmap in `docs/ROADMAP_CURRENT.md` and the technical context in `docs/AI_CONTEXT.md`.

## Mandatory Rules

- Do not start UI, NDI, encoder, audio, offscreen rendering, visual pipeline, or new composition features until the P0 GPU lifecycle hardening gates are complete and green.
- Execute roadmap work in the exact order defined in `docs/ROADMAP_CURRENT.md`.
- Do not keep dangerous APIs for compatibility.
- Do not introduce new `Simulate*` properties in production classes.
- Do not add TODOs in GPU lifetime, shutdown, dispose, keyed mutex, Vulkan registry, or submission ownership paths.
- Do not swallow physical resource finalization failures and mark success.
- Do not use native handles (`nint`, `DangerousGetHandleForInterop`) as logical texture identity.
- Do not call GPU wait APIs without explicit timeout.
- Every lifetime change must include tests.

## Current Technical Contract

- Submission cleanup must be `WaitForCompletionAsync(timeout, cancellationToken)` then `DisposeCompleted()`.
- `IRenderFrameSubmission` must not inherit or implement `IAsyncDisposable` or `IDisposable`.
- `IRenderBackend` must not expose synchronous `WaitIdle()`.
- `MediaForgeVulkanRenderer` must be internal and created through a public factory.
- Vulkan renderer test failures must use `IVulkanRendererFaultInjector`, not `Simulate*` properties.
- External texture identity is `VulkanExternalTextureKey = GpuTextureId + Width + Height + Format`.
- Provider lifecycle must be serialized through a single lifecycle gate.
- D3D11 ring finalization must fault `FullyDisposed` if any physical handle dispose fails.
- Legacy preview/capture paths must not be used as product paths.

## Required Validation

Run after each implementation unit:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

If the change touches Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry, render thread, provider, or submission, also run:

```powershell
./scripts/test.ps1 -Tier Gpu
```
