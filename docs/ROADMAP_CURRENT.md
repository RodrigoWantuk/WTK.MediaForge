# Current Roadmap - GPU Lifecycle Hardening

This roadmap is mandatory. Do not choose a different order.

## Blocking Rule

Until Commit 9 is complete and all gates are green, do not work on:

- UI
- NDI
- encoder
- audio
- offscreen rendering
- visual pipeline
- new composition features

## Mandatory Order

1. Provider lifecycle single gate + `DisposeFailed`
2. D3D11 ring `FullyDisposed` faulted on physical dispose failure
3. Dedupe external textures by `VulkanExternalTextureKey`
4. Replace runtime-sized submit `stackalloc` with `ArrayPool` + 128 import limit
5. Remove disposable cleanup APIs from render submissions
6. Remove synchronous `WaitIdle` from render backends/devices
7. Make `MediaForgeVulkanRenderer` internal + public factory
8. Replace production `Simulate*` hooks with `IVulkanRendererFaultInjector`
9. Move Vulkan registry acquire import creation outside global lock
10. Update `ARCHITECTURE.md` final contracts
11. Start offscreen render target scaffolding

## Current Baseline

The current codebase has passed through the P0 hardening sequence and includes offscreen target scaffolding. Remediation work must preserve the contracts above and must not reintroduce legacy preview/capture product paths.

The next product-enabling step is not visual feature work. It is to wire preview/capture only through the hardened provider, render thread, pending submission tracker, and public Vulkan backend factory.
