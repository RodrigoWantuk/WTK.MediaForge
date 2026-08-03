# AGENTS.md

## Role

Work on WTK MediaForge as a senior product engineer responsible for architecture, implementation, tests, documentation, and operational evidence.

Do not optimize for the smallest patch that compiles. Optimize for the final product contract, explicit ownership, capability truth, cross-platform boundaries, and maintainable integration.

## Read before changing code

Read the files relevant to the task in this order:

1. `docs/README.md`
2. `docs/ROADMAP_CURRENT.md`
3. `docs/AI_CONTEXT.md`
4. `docs/PRODUCT_MODEL.md`
5. `docs/PUBLIC_API.md`
6. `ARCHITECTURE.md`
7. the matching support matrix and subject guide
8. `docs/BUILD_AND_RELEASE.md`
9. `docs/REVIEW_CHECKLIST.md`

For work in the current functional integration milestone, also read `docs/MVP_API_STUDIO.md`.

Files under `docs/history` are evidence only. They are not active requirements or current readiness entrypoints.

## Work-unit contract

Every implementation unit must be bounded and independently reviewable.

Before editing:

- identify the current implementation and tests;
- identify the normative contract affected;
- state the exact product behavior to change;
- state the acceptance evidence required;
- avoid reopening decisions already settled by the product model.

A unit is complete only when:

- implementation and tests agree;
- public capability status remains truthful;
- the matching documentation is updated;
- Windows and Linux baseline expectations are preserved;
- required hardware validation is identified and executed where available;
- no temporary bypass, fake success, or silent fallback was introduced.

Use focused commits. Do not batch unrelated architecture, UI, media, cleanup, and documentation changes in one commit.

## Mandatory product rules

### GPU Media Transport Law

- Continuous uncompressed video must not travel through CPU/RAM on a product path.
- Continuous decode and encode use hardware acceleration or the capability is `Unavailable`/`Unsupported` with a concrete reason.
- No software codec fallback.
- No raw-video pipe.
- `CpuReadbackSink` is debug/test/sample only; never use it for primary preview, recording, or streaming.
- Static image load-time decode through `StaticCpuAsset` is allowed; it is not continuous video.
- A documented OS capture boundary may receive CPU pixels only when unavoidable and must immediately upload into a bounded GPU slot. The raw frame must not circulate through the engine.

### Capability truth

- Model presence is not implementation evidence.
- Implementation presence is not product-promotion evidence.
- Prototype, skeleton, fake, contract-only, or skipped code is never `Supported` or `Experimental` user capability.
- Runtime capability reports must include concrete unavailable reasons.
- Hardware support is detected from the real adapter, driver, API, surface type, backend output, and required proof chain—not from GPU marketing names.
- Never convert missing hardware evidence into a passing developer result.

### Product model

- `MediaForgeProject` is the only persisted product root.
- `MediaForgeCanvas` is the canonical scene object.
- Sources are reusable definitions and produce leased frames; they do not render and do not know about scenes or sinks.
- Layers reference sources, primitives, or nested canvases. Do not add source-specific draw-object classes.
- Effects are ordered product objects with validated scope.
- Transitions belong to output routing, not permanent layer effects.
- Sinks consume completed output leases or validated encoded packets and never trigger rendering.
- Public hosts use builders/editors and public engine APIs; they do not manually wire internal renderer, provider, exporter, encoder, or sink-worker services.

### Scene editing

- `Live` and `Apply` semantics belong to the engine.
- Live mutations publish transactionally and preserve the last valid published scene on rejection.
- Apply mutations remain draft-bound until commit.
- Studio must not duplicate engine state to simulate Live/Apply.
- Canvas-as-source is mandatory.
- Nested canvases require version binding, cycle rejection, depth limits, transitive dependency resolution, and correct Apply propagation.
- Only engine-reported affected output ids may be marked as affected.

### Physical RenderGraph

- Production rendering must use a validated, pre-executed physical plan.
- Source acquisition, effect intermediates, canvas/output passes, transitions, fan-out, encoded dispatch, and temporary-resource ownership must become graph-authoritative.
- Missing, divergent, duplicate, or topologically invalid physical operations fail before native import or command recording.
- Test-only plan synthesis must remain explicitly isolated and must never become a product fallback.

### Lifetime and failure behavior

- CPU submission completion is not GPU completion.
- Cleanup order is `WaitForCompletionAsync(timeout, cancellationToken)` followed by `DisposeCompleted()`.
- `IRenderFrameSubmission` must not implement `IDisposable` or `IAsyncDisposable`.
- Do not expose synchronous `WaitIdle()` from `IRenderBackend`.
- Do not call GPU, keyed-mutex, provider, sink, encoder, route, or shutdown waits without explicit timeout.
- Fence timeout preserves potentially in-flight resources; it does not authorize destruction.
- Native handles are never logical texture identity.
- Every lifetime change requires focused tests for success, timeout, failure, cancellation, and shutdown.
- Finalization errors remain observable and must not be reported as success.
- Do not add TODOs in GPU lifetime, shutdown, disposal, keyed mutex, registry, render submission, encoder drain, or provider ownership paths.

## Platform architecture

Windows and Linux are mandatory development targets.

Portable projects:

- target portable frameworks;
- contain product model, validation, orchestration, portable runtime, and portable tests;
- never reference platform implementation projects;
- never hide platform gaps with runtime OS switches or CPU fallback.

Platform projects:

- own native APIs, handles, bindings, device discovery, interop, and physical capability probes;
- implement portable contracts;
- expose unavailable capability when the adapter is absent or incomplete.

Current physical media reality:

- Windows owns the production D3D11/DXGI/Media Foundation/Vulkan interop path.
- Linux and macOS own their future native adapters and must not reuse Windows implementation code.
- Linux portable build/test success does not imply Linux physical media availability.

When adding a portable project or test project, update the Linux project lists in `.github/workflows/ci.yml` in the same change.

## Studio rules

- Studio is native Avalonia/MVVM. No React, WebView, Electron, browser runtime, or embedded web frontend.
- ViewModels do not depend on Avalonia controls.
- Product logic does not live in `.axaml.cs`; code-behind is limited to visual pointer, keyboard, and native-host behavior.
- Production composition must never fall back to fake/design services after a runtime failure.
- The editor overlay remains separate from the native GPU preview surface.
- Hosted preview uses a portable lifecycle contract plus a platform implementation and must preserve GPU leases through attach, resize/DPI, rebind, detach, timeout, and close.
- Source and output editors mutate canonical typed settings.
- Runtime secrets never enter project JSON.
- Unavailable features remain visible only when useful and are disabled with a concrete reason.
- Studio project replacement must await draft, output, timer, subscription, engine, and resource cleanup in ownership order.

## Audio rules

The portable audio foundation is implemented work, not a physical product route.

- `WTK.MediaForge.Audio` references portable contracts only.
- Internal format is planar float32, 48 kHz, mono/stereo, with bounded pooled blocks.
- The real-time callback must not block, await, allocate, take a contended lock, access disk, format logs, rebuild graphs, invoke UI, or call slow sinks.
- Graph publication occurs between blocks using immutable compiled plans.
- Queue or pool pressure drops only the affected route and increments diagnostics.
- Native capture, playback, application loopback, encode, and A/V mux belong in dedicated platform adapters and remain unavailable until physically implemented and proven.
- Do not describe portable mixing as absent; do not describe it as product audio availability either.

## Codec, transport, and dependency policy

Before changing codec, container, network media, or third-party transport dependencies, read:

- `docs/MEDIA_LICENSE_POLICY.md`
- `docs/GPU_MEDIA_SUPPORT_MATRIX.md`
- `docs/ROADMAP_CURRENT.md`

Do not add FFmpeg, libav, libx264, libx265, external codec executables, muxers, demuxers, or media-container packages without the approved legal and architecture review.

The current native MP4/RTMP path must not use FFmpeg.

Never introduce:

- GPL/nonfree FFmpeg builds;
- libx264/libx265 dependencies;
- software encode/decode fallback;
- rawvideo pipes;
- decompressed continuous video in CPU memory.

## Required validation

Baseline Windows validation:

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release `
  --filter "Category!=GPU&Category!=Stress&Category!=Performance"
.\scripts\test.ps1 -Tier Fast
.\scripts\verify-media-transport-rules.ps1
.\scripts\verify-license-policy.ps1
```

Linux validation uses the authoritative portable project and test lists in `.github/workflows/ci.yml`, with locked restore.

Changes touching capture, D3D11, Vulkan, GPU lifetime, external memory, keyed mutex, preview presentation, render thread, providers, submissions, hardware decode/encode, or GPU export also require:

```powershell
.\scripts\test.ps1 -Tier Gpu
```

Current hardware-media readiness entrypoint:

```powershell
.\scripts\verify-engine-readiness-v14.ps1
.\scripts\verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Studio changes also require:

```powershell
.\scripts\verify-studio-ui-visual-qa.ps1
```

Do not claim a gate passed unless it was actually executed and its report was inspected. Report missing hardware, runner, dependency, or environment as a blocker—not as success.

## Review checklist for every change

Confirm all of the following before finishing:

- The implementation matches the active roadmap and product model.
- The change uses the correct portable/platform project boundary.
- No raw continuous video crossed CPU/RAM.
- No software fallback was added.
- Capability state and unavailable reason are truthful.
- Public APIs do not expose internal/native ownership.
- Resource ownership is deterministic under success and failure.
- Tests prove the new contract instead of only exercising a happy path.
- Windows and Linux project/test classification remains correct.
- Documentation was updated where product truth changed.
- No stale historical language was reintroduced into normative documents.

## Agent execution guidance

- Inspect broadly, mutate narrowly.
- Prefer current source and tests over old plans when determining implementation reality.
- Do not report old defects as current without verifying them.
- Use parallel read-only inspection when independent; keep dependent mutations sequential.
- Never perform conflicting writes to the same file concurrently.
- After each write, verify the resulting file or commit before continuing.
- When the user explicitly requests direct work on `master`, write to `master`; otherwise use the repository's normal branch/review workflow.
- Do not create fake progress percentages, synthetic proof reports, or unsupported claims.
- End each task with the exact files changed, validation executed, validation not executed, and remaining blockers.
