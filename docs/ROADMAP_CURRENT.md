# Current Roadmap - GPU Lifecycle + Public API

This roadmap is mandatory. Do not choose a different order within each track.

## Status

- **P0 GPU lifecycle (commits 1-11):** complete
- **Product model formalization (H1-H7):** H1 complete; H2-H7 pending
- **Public API stabilization (PAPI-1-PAPI-8):** PAPI-1 in progress
- **Visual compositing + real sources/outputs:** blocked until PAPI-8

## Blocking Rule (Product Features)

Until public API commits PAPI-1-PAPI-8 are complete, do not implement:

- UI shells beyond test harnesses
- NDI, RTSP, webcam, MP4 decode sources
- encoder, audio, streaming sinks
- productive WinForms preview binding
- ad hoc draw object types per media format
- public plugin APIs

Documentation and API contract work is allowed and required.

## Completed - P0 GPU Lifecycle

1. Provider lifecycle gate + DisposeFailed
2. Ring FullyDisposed faulted
3. Dedupe by VulkanExternalTextureKey
4. ArrayPool + limit 128 imports
5. Remove IAsyncDisposable from submissions
6. Remove synchronous WaitIdle from IRenderBackend
7. MediaForgeVulkanRenderer internal + factory-controlled creation
8. IVulkanRendererFaultInjector (no Simulate*)
9. Registry acquire outside global lock
10. ARCHITECTURE.md final contracts
11. Offscreen render target scaffolding

## Current - Public API Stabilization (PAPI-1-PAPI-8)

See [PUBLIC_API.md](PUBLIC_API.md) for the public product API boundary.

| # | Commit |
|---|--------|
| PAPI-1 | Public API audit |
| PAPI-2 | `MediaForgeWindows.CreateEngine` |
| PAPI-3 | `MediaForgeProjectBuilder` |
| PAPI-4 | Public engine state/runtime API |
| PAPI-5 | Typed `RenderOutputTarget` contracts |
| PAPI-6 | Public validation/runtime exceptions |
| PAPI-7 | Public engine events |
| PAPI-8 | Offscreen sample |

## Deferred - Product Model (H2-H7)

See [PRODUCT_MODEL.md](PRODUCT_MODEL.md) for full contract.

| # | Commit |
|---|--------|
| H2 | Source type catalog + typed settings |
| H3 | Output type catalog + typed settings |
| H4 | Effect model |
| H5 | MediaForgeProjectEditor |
| H6 | Advanced graph validation (cycles, depth 8) |
| H7 | MediaForgeEngine facade skeleton |

## After PAPI-8

1. Minimal compositing hardening beyond CP1.
2. CP2 multi-layer basics.
3. CP3 nested canvas.
4. Real source/output integrations after the public API is stable.

## Validation Gates

After each code commit:

```powershell
dotnet test
./scripts/test.ps1 -Tier Fast
```

When touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry, render thread, provider, or submission, also run:

```powershell
./scripts/test.ps1 -Tier Gpu
```
