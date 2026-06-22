# Current Roadmap - Sprint 0 Hardening, Then CP2

This roadmap is mandatory. Do not choose a different order inside the active
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
- **Sprint 0 hardening:** active
- **CP2/multi-layer renderer track:** starts after Sprint 0 validation is green

## Blocking Rule

Do not implement the following until the roadmap step that owns it is active:

- CP3 nested canvas compositor
- chroma/effect rendering
- productive WinForms preview
- UI shells beyond test harnesses
- NDI, RTSP, webcam, MP4 decode sources
- encoder, audio, recording, streaming sinks
- public plugin APIs

Documentation, tests, API contract work, Sprint 0 hardening, and the CP2
renderer track listed below are allowed.

## Active Commit Order

1. **Sprint 0.1 - Render pump wait cleanup**
   - Replace competing delay/wake tasks with one timeout-aware wake wait.
   - Cover prompt request, stop cancellation, and no pending wait accumulation.
2. **Sprint 0.2 - Sink attach rollback timeout**
   - Roll back failed sink attach with bounded cleanup, diagnostics, and
     aggregated failures.
3. **Sprint 0.3 - Sink enqueue signal safety**
   - Ensure enqueue signaling failures return failure and release/drain leases.
4. **Sprint 0.4 - Rendered surface/batch guards**
   - Keep real backends tied to rendered surface leases while preserving the
     null backend test path.
5. **CP2.1 - Same source, multiple layers**
   - Render one source through more than one layer without double acquiring or
     double releasing source frames.
6. **CP2.2 - Multiple sources, order, alpha, transforms**
   - Render multiple source layers in canvas order with opacity and alpha blend.
7. **CP2.3 - Unsupported draw object/effect diagnostics**
   - Report unsupported draw objects, effects, and blend modes explicitly.
8. **CP2.4 - CP2 stress and lifetime coverage**
   - Validate repeated submissions, descriptor lifetime, offscreen surfaces, and
     source leases under sustained multi-layer load.

## Completed - GPU Lifecycle

1. Provider lifecycle gate + DisposeFailed
2. Ring FullyDisposed faulted
3. Dedupe by `VulkanExternalTextureKey`
4. ArrayPool + limit 128 imports
5. Remove `IAsyncDisposable` from submissions
6. Remove synchronous `WaitIdle` from `IRenderBackend`
7. `MediaForgeVulkanRenderer` internal + factory-controlled creation
8. `IVulkanRendererFaultInjector`, no `Simulate*` production switches
9. Registry acquire outside global lock
10. Architecture final contracts
11. Offscreen render target scaffolding
12. CP1 framebuffer/descriptor/offscreen target submission lifetime
13. CP1 source/output layout rollback and shader-read preservation
14. Registry import failure propagation without silent retry

## Completed - Runtime Foundations

1. Project updates clone and swap only after validation.
2. Start/Stop/Dispose use explicit timeouts and deterministic cleanup.
3. `CurrentProject` returns a clone.
4. Render pump publishes continuous frames and reports backpressure.
5. Public sink contracts and bounded per-sink queues exist.
6. One output can feed multiple sinks through fanout leases.
7. Sink detach/dispose uses explicit stop timeouts.
8. Source runtime manager isolates provider lifecycle and acquisition.
9. Source frame buffers own lease-based `KeepLatest`, `Queue`,
   `TimelineDriven`, and `Static` behavior.
10. Source acquire failures report diagnostics and produce missing-frame layers.

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
