# Current Roadmap - Post-CP2 Output and Composition

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
- **Sprint 0 hardening:** complete
- **CP2/multi-layer renderer track:** complete
- **First public visual output sink:** complete
- **CP3 solid layer:** complete
- **CP3 nested canvas:** complete
- **CP3 first effect:** active

## Blocking Rule

Do not implement the following until the roadmap step that owns it is active:

- productive WinForms preview
- UI shells beyond test harnesses
- NDI, RTSP, webcam, MP4 decode sources
- encoder, audio, recording, streaming sinks
- public plugin APIs

Documentation, tests, API contract work, the public CpuReadbackSink, Solid
layers, CP3 nested canvas, and the first ChromaKeyEffect listed below are
allowed.

## Active Commit Order

1. **Sprint 3.3 - ChromaKeyEffect**
   - Implement the first real source-layer effect while preserving explicit
     diagnostics for unsupported effects and invalid effect configuration.

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

## Completed - Sprint 0 Hardening

1. Render pump wait cleanup uses a single timeout-aware wake wait.
2. Sink attach rollback cleanup is bounded, diagnostic, and aggregated.
3. Sink enqueue signaling failures release/drain leases and report failure.
4. Real backend output frame coverage asserts rendered surface ownership while
   preserving the null backend snapshot test path.

## Completed - CP2 Multi-layer Renderer

1. Same source can be used by multiple layers without double acquisition or
   double release.
2. Multiple sources render in canvas order with normal alpha, opacity, disabled
   layer handling, opacity-zero skipping, and transforms.
3. Unsupported draw objects, effects, and blend modes emit explicit diagnostics.
4. Repeated CP2 submissions cover descriptor, framebuffer, offscreen surface,
   and source lease lifetime.

## Completed - First Public Visual Output Sink

1. `CpuReadbackSink` delivers owned CPU pixel buffers with stride, format, size,
   frame number, and timestamp.
2. CPU readback is routed through an internal backend surface capability and
   does not expose Vulkan or D3D11 handles publicly.
3. Vulkan output targets are replaced before submit when an earlier rendered
   surface still has live submission or sink references, preserving frame
   content for slow sinks.

## Completed - CP3 Solid Layer

1. `SolidDrawObject` renders in Vulkan using the solid fragment shader.
2. Solid layers support transform, clipping, opacity, and normal alpha blending.
3. Solid no longer emits `render.drawobject_not_supported`; unsupported
   diagnostics remain for text, nested canvas, unsupported effects, and
   unsupported blend modes.

## Completed - CP3 Nested Canvas

1. `CanvasDrawObject` renders child canvases into submission-retained
   intermediate Vulkan targets and composites them into the parent canvas.
2. Nested canvas rendering supports transform, opacity, normal alpha blending,
   and the established depth-8 contract.
3. Intermediate targets are retained by `VulkanSubmissionResourceScope` until
   the submitted command buffer fence completes.

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
