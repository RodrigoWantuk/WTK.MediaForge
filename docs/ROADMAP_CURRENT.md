# Current Roadmap - Public API Before Product Features

This roadmap is mandatory. Do not choose a different order within each active
track.

## Status

- **P0 GPU lifecycle:** complete
- **Engine transactional/shutdown hardening:** complete
- **CP1 visual correctness proof:** complete for first source/offscreen path
- **CP2 infrastructure preflight:** descriptor capacity and registry waiter timeout complete
- **Product model formalization (H1-H7):** foundation complete
- **Public API stabilization (PAPI-1-PAPI-8):** PAPI-1 complete, PAPI-2 next
- **CP2/multi-layer and product integrations:** blocked until PAPI is complete or this roadmap is explicitly changed

## Blocking Rule

Do not implement the following until PAPI-2 through PAPI-8 are complete:

- CP2 multi-layer compositor
- CP3 nested canvas compositor
- chroma/effect rendering
- productive WinForms preview
- UI shells beyond test harnesses
- NDI, RTSP, webcam, MP4 decode sources
- encoder, audio, recording, streaming sinks
- public plugin APIs

Documentation, tests, and API contract work remain allowed.

## Completed - GPU Lifecycle

1. Provider lifecycle gate + DisposeFailed
2. Ring FullyDisposed faulted
3. Dedupe by VulkanExternalTextureKey
4. ArrayPool + limit 128 imports
5. Remove IAsyncDisposable from submissions
6. Remove synchronous WaitIdle from IRenderBackend
7. MediaForgeVulkanRenderer internal + factory-controlled creation
8. IVulkanRendererFaultInjector, no Simulate* production switches
9. Registry acquire outside global lock
10. ARCHITECTURE.md final contracts
11. Offscreen render target scaffolding
12. CP1 framebuffer/descriptor/offscreen target submission lifetime
13. CP1 source/output layout rollback and shader-read preservation
14. Registry import failure propagation without silent retry

## Completed - Engine Hardening

1. `ApplyProjectUpdateAsync` edits a deep clone and swaps only after validation.
2. Invalid project updates do not mutate `CurrentProject`, replace project snapshots, or publish frames.
3. Valid running updates replace the project and publish a new frame.
4. `BindOutputAsync` is transactional across sink creation, binding creation, enqueue, and swap.
5. Failed bind keeps the previous sink and disposes the failed new sink.
6. `UnbindOutputAsync` enqueues unbind before sink disposal and removes registration even if disposal fails.
7. `StopAsync` skips backend disposal when render thread is still alive and reports a fatal diagnostic.
8. Backend disposal still runs when render-thread cleanup failed after the thread stopped.

## Completed - CP1 Visual/Infra Hardening

1. Source-layer Fit outputs transparent pixels outside the fitted content area.
2. Output pass copies final canvas/letterbox pixels without double alpha blending.
3. Offscreen readback helper verifies real GPU pixels in tests.
4. Tests cover center pixel, Fit transparent bars, Fill, Stretch, opacity, and output letterbox color.
5. CP1 descriptor pool uses an explicit per-submit capacity instead of magic 16.
6. Registry waiters use timeout diagnostics instead of blocking indefinitely.
7. Many-layer CP1 submit infrastructure does not exhaust descriptors.

## Current - Public API Stabilization

See [PUBLIC_API.md](PUBLIC_API.md) for the public product API boundary.

| # | Commit | Status |
|---|---|---|
| PAPI-1 | Public API audit | Complete |
| PAPI-2 | `MediaForgeWindows.CreateEngine` | Next |
| PAPI-3 | `MediaForgeProjectBuilder` | Pending |
| PAPI-4 | Public engine state/runtime API | Pending |
| PAPI-5 | Typed `RenderOutputTarget` contracts | Pending |
| PAPI-6 | Public validation/runtime exceptions | Pending |
| PAPI-7 | Public engine events | Pending |
| PAPI-8 | Offscreen sample | Pending |

PAPI must make the supported external usage possible without exposing:

- `CompositionRuntime`
- `MediaForgeRenderThread`
- `RenderThreadGuard`
- backend factories
- output sink factories
- source provider factories
- Vulkan/D3D11 internals
- manual `JsonObject` settings for normal authoring

## Product Model Foundation

See [PRODUCT_MODEL.md](PRODUCT_MODEL.md) for the product contract.

| # | Deliverable | Status |
|---|---|---|
| H1 | Product model document | Complete |
| H2 | Source type catalog + typed settings | Complete foundation |
| H3 | Output type catalog + typed settings | Complete foundation |
| H4 | Effect model | Complete foundation |
| H5 | `MediaForgeProjectEditor` | Complete foundation |
| H6 | Graph validation, cycles, depth 8 | Complete foundation |
| H7 | Engine facade skeleton | Complete foundation |

## After PAPI-8

1. Reassess CP2 scope with current tests green.
2. CP2 multi-layer basics.
3. CP3 nested canvas with target cache/lifetime rules.
4. First real effect rendering.
5. Product integrations only after renderer/API contracts are stable.

## Validation Gates

After each code commit:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

When touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry,
render thread, provider, or submission, also run:

```powershell
./scripts/test.ps1 -Tier Gpu
```
