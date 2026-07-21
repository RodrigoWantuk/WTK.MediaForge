# Review Checklist

Before considering a change complete, verify:

## General

- [ ] No new UI, NDI, encoder, audio, offscreen, or visual pipeline work before required gates.
- [ ] No new TODOs in lifetime paths.
- [ ] No swallowed exceptions in physical resource cleanup.
- [ ] Tests added for every lifetime change.
- [ ] Runtime/GPU/snapshot internals are not exposed as public product API.
- [ ] Public API changes update `docs/PUBLIC_API.md` and `Public_api_matches_approved_allowlist`.
- [ ] Prototype, planned, blocked, unsupported, debug-only, or non-product capabilities/sinks have user-visible unavailable reasons.
- [ ] CP2, nested canvas, preview, NDI, encoder, webcam, RTSP, MP4, streaming, or audio work starts only after the roadmap explicitly opens that track.
- [ ] New source/output types are introduced first as product contracts, not hidden runtime integrations.
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
- [ ] Text atlas uploads use explicit GPU wait timeout and release staging buffer/memory on partial failures.
- [ ] Text atlas cache keys include text, font family, and size.
- [ ] Source-layer blur retains intermediate targets/descriptors/framebuffers until submission fence completion.
- [ ] Output route fade transitions use explicit frame delta time, not wall-clock drift.
- [ ] Output transition passes use load/blend render pass state only after the previous output pass has completed command recording for the same submission.

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
- [ ] Sink attach timeout is owned by the sink dispatcher; engine wrappers must not abandon dispatcher cleanup before the sink observes cancellation.

## Scene Routing, Packages, And Render Graph

- [ ] Public `Scene` APIs remain aliases over `MediaForgeCanvas`, not a competing scene primitive.
- [ ] Live scene edits mutate the published canvas only after validation and request a new frame for sinks.
- [ ] Apply scene edits mutate only the draft session until `ApplySceneDraftAsync` commits.
- [ ] Discarding an Apply draft leaves the published project unchanged and removes draft runtime state.
- [ ] Canvas-as-source uses `SceneVersionBinding`; normal outputs use published binding, draft previews use draft binding, and transitions may use explicit versions.
- [ ] Explicit scene version binding resolves an immutable `SceneVersionId -> CanvasStateSnapshot`, not the current canvas with an old version id.
- [ ] Scene dirty/version fingerprints include typed effect parameters, not only effect type/order/enabled state.
- [ ] Apply in a nested canvas computes direct/transitive canvas consumers and affected output routes before transition execution.
- [ ] RenderGraph/cache keys include canvas version binding; no cache key relies only on canvas id and size.
- [ ] Physical RenderGraph validation rejects duplicate keys, missing/forward dependencies, unknown canvases/outputs, and missing output passes before Vulkan command recording.
- [ ] Routes use `CanvasId -> RenderOutput -> RenderOutputSink(s)` and never direct canvas-to-encoder/NDI/preview shortcuts.
- [ ] NDI Standard SDK runtime detection/source discovery is not treated as video product support; NDI source/output video paths remain blocked unless GPU-safe product proofs pass.
- [ ] Same scene routed to multiple sinks/outputs does not require duplicate canvas rendering when size/config/version match.
- [ ] Same source across scenes/layers is acquired once per frame where the runtime graph can share it.
- [ ] Reusable source effect-chain nodes are keyed by semantic source/effect configuration, not by runtime object identity.
- [ ] Different output sizes reuse the same canvas render where possible and split only output-fit/presentation passes.
- [ ] Submitted render snapshots carry graph execution computed after source-frame acquisition, not a pre-acquisition dry run.
- [ ] Package JSON contains schema version, ids, type ids, typed settings, transforms, effects, canvas graph, routes, and metadata only.
- [ ] Package JSON does not contain runtime leases, native handles, Vulkan/D3D11 objects, command buffers, fences, backend worker state, sink queues, or secrets by default.
- [ ] Import validates schema, ids, missing references, output routes, unsupported types, migrations, and canvas cycles before returning an applied candidate.
- [ ] Dry-run import and failed import do not mutate the existing project or engine state.

## Render Output Sinks

- [ ] Public output consumption uses `RenderOutput -> RenderOutputSink(s)`, not direct canvas-to-preview/encoder/NDI paths.
- [ ] Public sink APIs do not expose Vulkan images, D3D11 textures, raw shared handles, render-thread types, snapshots, or GPU frame slots.
- [ ] Sink callbacks run outside the render thread.
- [ ] Each sink has bounded queue/backpressure behavior.
- [ ] Sink attach failure rolls back without leaving a partially registered sink or surface.
- [ ] Sink detach stops delivery, stops the sink, disposes it, and removes automatic surface bindings when no sinks remain.
- [ ] One `RenderOutput` can feed multiple sinks without rendering the same output more than once per frame.
- [ ] `PreviewPanelSink` does not remove a Win32 panel presenter while a presentation is in flight; timeout/cancellation is observable and preserves the presenter.

## Texture Identity

- [ ] Dedupe uses `VulkanExternalTextureKey`.
- [ ] No dedupe by `nint` or `DangerousGetHandleForInterop`.

## Provider Lifecycle

- [ ] `StartAsync`, `StopAsync`, `DisposeAsync`, and `Dispose` use one lifecycle gate.
- [ ] `DisposeFailed` exists.
- [ ] Non-timeout failure does not leave `Disposing`.
- [ ] Retry after `DisposeTimedOut` or `DisposeFailed` works when the failed resource is recoverable.
- [ ] Desktop duplication reconnect replaces GPU slot rings created on the old D3D11 device before publishing frames from the new session.
- [ ] Failed reconnect marks the provider failed and emits diagnostics instead of leaving the source apparently running.
- [ ] Desktop/window capture reconnect stops and disposes superseded or failed native sessions instead of leaking them.
- [ ] Window capture is not advertised as available until the real HWND -> WGC -> engine-owned D3D11 slot -> Vulkan proof passes.
- [ ] Webcam is not advertised as available until the immediate GPU-upload provider is product validated.

## D3D11 Ring

- [ ] Dispose attempts all slots.
- [ ] Any physical dispose failure faults `FullyDisposed`.
- [ ] Manager moves faulted resources to failed state.

## GPU Media Transport

- [ ] No product source/sink/encoder uses continuous raw CPU video frames without `RawCpuVideoFrameExceptionAttribute`.
- [ ] Static image load uses `MediaTransportKind.StaticCpuAsset`; not counted as raw CPU video exception.
- [ ] `CpuReadbackSink` is not wired as recording, streaming, or primary preview path.
- [ ] Product recording/streaming sinks consume encoded packets after hardware encode only.
- [ ] Encoded H.264 packets declare Annex-B/AVCC format and sinks reject unknown bitstreams instead of guessing.
- [ ] FFmpeg is not used in first MP4/RTMP product paths.
- [ ] Continuous video decode/encode paths use validated hardware acceleration or report unavailable; no software fallback or CPU staging is introduced.
- [ ] OS-specific media adapters stay in OS-specific projects and capability reports expose runtime-detected backend/vendor status.
- [ ] libx264/libx265 appear as Prohibited in capability/license matrix.
- [ ] v14 render-to-encode, hardware encode, MP4 recording, MP4 input/output, webcam input, window capture input, RTMP network output, and NDI input/output proofs pass before those media I/O paths are promoted.
- [ ] `scripts/generate-media-proof-report.ps1` writes `test-reports/media-proof-report.json` and `test-reports/media-proof-report.md` with explicit reasons for every non-passed proof or feature.
- [ ] `IMediaTransportAuditSink` proves product encode path without `CpuReadbackAttempted` or `StagingBufferCreated`.
- [ ] Product MP4 recording and public RTMP streaming reject packets without trusted `BackendOutputValidated` evidence; public callers cannot forge that evidence through packet initializers.
- [ ] BGRA/RGBA -> NV12 encoder format conversion uses a GPU backend path or records explicit unavailable diagnostics; no CPU staging fallback.
- [ ] Hardware encoder `DisposeAsync` cannot silently replace an omitted `DrainAsync`; accepted input without a completed drain fails finalization while owned D3D11 resources are still released.
- [ ] Capability report is consumable via `GetCapabilityReportAsync` with status and reason per feature.
- [ ] `Supported` and `Experimental` capability entries are the only user-available states; every unavailable entry includes a non-empty reason.
- [ ] Guard rails (`verify-media-transport-rules.ps1`) pass on Fast tier.
- [ ] License policy verification (`verify-license-policy.ps1`) passes.
- [ ] Engine readiness v14 (`verify-engine-readiness-v14.ps1`) is the only official hardware-first gate; it performs locked restore, serializes GPU ownership across test assemblies, rejects contradictory aggregate/proof status, tests encoded output profiles, adapter LUID identity and Media Foundation ownership, and regenerates the proof report.
- [ ] Scripts v4-v12 under `docs/history/readiness-scripts` are not executed as current gates.
- [ ] Release hardware media readiness uses the current readiness script with `-RequireHardwareMedia`; missing required proofs must fail with an actionable report, not silent success.
- [ ] Preview remains Experimental until hosted resize and repeated timeout-recovery evidence passes.
- [ ] Performance evidence executes real engine/GPU work; no `Task.Delay` scenario is accepted.
- [ ] Readiness v14 runs a 1080p60 sustained-route smoke; release sign-off also runs `-RunLocalQualification` and `-ReleaseCandidateQualification` on each required adapter family.

## FFmpeg / external codec review checklist

For any PR touching media container, codec, demux, mux, encode, decode, recording, streaming, or packetization:

- [ ] The PR does not call `ffmpeg.exe`.
- [ ] The PR does not pipe `rawvideo`.
- [ ] The PR does not use `libx264`.
- [ ] The PR does not use `libx265`.
- [ ] The PR does not introduce GPL/nonfree FFmpeg builds.
- [ ] The PR does not perform software video encode/decode in product runtime.
- [ ] FFmpeg libraries, if referenced, operate only on encoded packets/container metadata.
- [ ] `docs/MEDIA_LICENSE_POLICY.md` was updated.
- [ ] `docs/GPU_MEDIA_SUPPORT_MATRIX.md` was updated.
- [ ] `scripts/verify-license-policy.ps1` passes.
- [ ] `scripts/verify-media-transport-rules.ps1` passes.
