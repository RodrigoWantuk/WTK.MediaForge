# Current Roadmap - Runtime/Sink Foundation Before CP2

This roadmap is mandatory. Do not choose a different order within each active
track.

## Status

- **P0 GPU lifecycle:** complete
- **Engine transactional/shutdown hardening:** complete
- **CP1 visual correctness proof:** complete for first source/offscreen path
- **CP2 infrastructure preflight:** descriptor capacity and registry waiter timeout complete
- **Product model formalization (H1-H7):** foundation complete
- **Public API stabilization (PAPI-1-PAPI-8):** complete
- **Public runtime/sink foundation:** complete for safe project ownership, timeouts, render pump, and completed-frame notification sink
- **Source runtime/buffer foundation:** complete for internal source runtime ownership, lease-buffer primitives, source acquire diagnostics, and engine integration
- **Sink queue foundation:** complete for extracted bounded per-sink queue policy and fanout lease coverage
- **CP2/multi-layer and product integrations:** blocked until this roadmap explicitly starts the next renderer track

## Blocking Rule

Do not implement the following until the roadmap explicitly starts the next renderer track:

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
9. `CurrentProject` returns a clone and cannot expose engine-owned mutable state.
10. `StartAsync` requires a loaded project; `StopAsync` returns to `Loaded` when the project remains loaded.
11. `StartTimeout`, `CommandTimeout`, and `StopTimeout` are applied to public long-running operations.
12. A continuous render pump publishes frames while running and reports backpressure drops.

## Completed - CP1 Visual/Infra Hardening

1. Source-layer Fit outputs transparent pixels outside the fitted content area.
2. Output pass copies final canvas/letterbox pixels without double alpha blending.
3. Offscreen readback helper verifies real GPU pixels in tests.
4. Tests cover center pixel, Fit transparent bars, Fill, Stretch, opacity, and output letterbox color.
5. CP1 descriptor pool uses an explicit per-submit capacity instead of magic 16.
6. Registry waiters use timeout diagnostics instead of blocking indefinitely.
7. Many-layer CP1 submit infrastructure does not exhaust descriptors.
8. Canvas background color is used by the CP1 canvas render pass.
9. Source layer scissor is clipped to canvas bounds; fully outside layers draw nothing.

## Completed - RenderOutput Sink Foundation

1. Public sink contracts exist under `WTK.MediaForge.Composition.Outputs`.
2. `AttachSinkAsync` and `DetachSinkAsync` attach public sinks to a `RenderOutput`.
3. Internal `RenderOutputSinkDispatcher` uses bounded per-sink queues and does not run sink work on the render thread.
4. Sink backpressure and frame delivery failures are reported through diagnostics.
5. One `RenderOutput` can feed multiple sinks without rendering the canvas more than once per frame.
6. `FrameNotificationSink` provides the first public sample/test sink.
7. Public output targets moved out of `Runtime.Outputs`; `WinFormsPreviewRenderOutputTarget` rejects a zero window handle.
8. `RenderOutputSinkQueue` owns bounded `KeepLatest`, `DropOldest`, and `DropNewest` queue behavior.
9. Fanout leases keep one output frame alive until every consuming sink releases its lease.

## Completed - Source Runtime Foundation

1. `SourceRuntimeManager` and `MediaSourceRuntime` isolate provider lifecycle/acquisition from the engine.
2. `SourceFrameBuffer` provides internal lease-based source buffering primitives for `KeepLatest`, `Queue`, `TimelineDriven`, and `Static` modes.
3. `KeepLatest` and `Static` reuse the last valid frame across render ticks until a newer frame replaces it or the runtime is cleaned up.
4. `Queue` consumes frames in order and releases each dropped or drained lease.
5. Existing render snapshots acquire source frames through the runtime manager instead of directly from raw providers.
6. Source acquire failures report diagnostics and produce a missing-frame layer instead of crashing snapshot build/render pump.
7. Source manager start rollback and stop aggregation are covered by tests.

## Completed - Public API Stabilization

See [PUBLIC_API.md](PUBLIC_API.md) for the public product API boundary.

| # | Commit | Status |
|---|---|---|
| PAPI-1 | Public API audit | Complete |
| PAPI-2 | `MediaForgeWindows.CreateEngine` | Complete |
| PAPI-3 | `MediaForgeProjectBuilder` | Complete |
| PAPI-4 | Public engine state/runtime API | Complete |
| PAPI-5 | Typed `RenderOutputTarget` contracts | Complete foundation |
| PAPI-6 | Public validation/runtime exceptions | Complete |
| PAPI-7 | Public engine events | Complete |
| PAPI-8 | Offscreen sample | Complete |

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

## Current - After Runtime/Sink Foundation

1. Reassess CP2 scope with current tests green.
2. CP2 multi-layer basics: multiple source layers, list/z order, opacity, alpha blend, one canvas, one offscreen output.
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
