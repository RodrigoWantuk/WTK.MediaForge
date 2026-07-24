# AGENTS.md

## Project Role

You are working on WTK MediaForge, a GPU-first media compositor/capture/render engine.

Act as a senior technical implementer. Follow the current roadmap in `docs/ROADMAP_CURRENT.md` and the technical context in `docs/AI_CONTEXT.md`.

## Mandatory Rules

- Follow the product roadmap in `docs/ROADMAP_CURRENT.md`; do not follow historical gate language from older acceptance notes when it conflicts with the current roadmap.
- **Windows and Linux are mandatory development targets.** Every new implementation must be designed for both platforms unless it is an explicitly isolated OS adapter.
- Portable projects must target portable TFMs and must not reference `WTK.MediaForge.Windows`, Win32-only APIs, D3D11-only implementation types, or Windows-specific packages.
- OS-specific behavior belongs in the corresponding platform project behind portable contracts. Do not place a Windows fallback, Linux fallback, or runtime OS switch in Core merely to make a build pass.
- Every implementation unit must include tests designed for the Windows and Linux execution model. Portable behavior must be covered by tests that compile and run on both CI runners; platform-specific behavior must have dedicated tests in the matching platform test project.
- A change is not complete while either the `Windows build and tests` or `Linux build and tests` CI job is failing, skipped unexpectedly, or not applicable because the project graph was incorrectly classified.
- Advance media features only when the implementation preserves GPU lifetime, hardware media transport, capability truth, and explicit failure diagnostics.
- Do not keep dangerous APIs for compatibility.
- Do not introduce new `Simulate*` properties in production classes.
- Do not add TODOs in GPU lifetime, shutdown, dispose, keyed mutex, Vulkan registry, or submission ownership paths.
- Do not swallow physical resource finalization failures and mark success.
- Do not use native handles (`nint`, `DangerousGetHandleForInterop`) as logical texture identity.
- Do not call GPU wait APIs without explicit timeout.
- Every lifetime change must include tests.
- **GPU Media Transport Law**: no uncompressed continuous video frames in CPU/RAM on the product path.
- No software decode/encode fallback for continuous video; hardware/GPU path or `Unsupported`.
- `CpuReadbackSink` is debug/test only; never wire as recording, streaming, or primary preview.
- Static image load uses `StaticCpuAsset` (load-time CPU decode, GPU render); not a `RawCpuVideoFrameException`.
- FFmpeg is deferred until the native hardware MP4/RTMP product path is sustained and a separate encoded-packet/container-only legal review approves the scope.
- Hardware MP4/RTMP routes require composite runtime proofs: render-to-encode, hardware encode, packet mux/transport, and sustained route validation.
- Scene editing semantics belong to the engine. `Live` edits update published
  scene state transactionally; `Apply` edits stay in draft state until commit.
  Do not make Studio duplicate projects to simulate this behavior.
- Canvas-as-source is a product feature. Nested canvas references must carry
  version binding, detect cycles/depth, and propagate Apply commits to affected
  output routes.
- Capability probing uses `IHardwareMediaCapabilityProbe.ProbeAsync`; never block the UI thread.

## Studio UI Exception

A limited Avalonia Studio track is allowed when it follows `docs/UI_STUDIO_DESIGN.md`, `docs/UI_IMPLEMENTATION_PLAN.md`, and `docs/UI_ACCEPTANCE_CHECKLIST.md`.

This exception is UI-only. It permits dark-theme shell layout, mock/design data, Project Explorer, preview mock, Inspector, Bottom Workbench, diagnostics/performance/output-monitor mock panels, status bar, and fake command state.

It does not permit real capture adapters, real media adapters, real recording/streaming/NDI/virtual-camera sinks, real audio pipeline work, real GPU preview integration before the approved preview reliability gate, or any legacy direct preview/capture path.

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

The automatic `cross-platform-ci` workflow runs for every push and for pull requests targeting `master`. It is the required baseline gate and always launches both:

- `Windows build and tests`: full solution Release restore/build, portable tests, Fast gate, transport rules, and license policy;
- `Linux build and tests`: locked restore/build of the portable project set and the complete portable test set.

Run after each implementation unit before pushing:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

When developing on Linux, restore/build/test the portable projects affected by the change using `--locked-mode`; the authoritative project list is maintained in `.github/workflows/ci.yml`.

If the change touches Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry, render thread, provider, or submission, also run on the appropriate hardware host:

```powershell
./scripts/test.ps1 -Tier Gpu
```

Do not merge or move work to `master` until both automatic OS jobs pass. Hardware-specific qualification remains an additional gate and never substitutes for the Windows/Linux baseline.

## FFmpeg and codec policy

Do not add FFmpeg, libav*, libx264, libx265, codec libraries, muxers, demuxers, or media container packages without checking:

- `docs/MEDIA_LICENSE_POLICY.md`
- `docs/GPU_MEDIA_SUPPORT_MATRIX.md`
- `docs/ROADMAP_CURRENT.md`

FFmpeg is not allowed in the native MP4/RTMP hardware product path.

Future FFmpeg library usage is limited to encoded-packet/container operations after the dedicated **FFmpeg Libraries Integration Review** phase.

Never implement:

- rawvideo pipe;
- software video encode fallback;
- software video decode fallback;
- raw decompressed video frames crossing CPU/RAM;
- GPL/nonfree FFmpeg build;
- libx264/libx265 dependency.
