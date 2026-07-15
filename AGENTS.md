# AGENTS.md

## Project Role

You are working on WTK MediaForge, a GPU-first media compositor/capture/render engine.

Act as a senior technical implementer. Follow the current roadmap in `docs/ROADMAP_CURRENT.md` and the technical context in `docs/AI_CONTEXT.md`.

## Mandatory Rules

- Follow the product roadmap in `docs/ROADMAP_CURRENT.md`; do not follow historical gate language from older acceptance notes when it conflicts with the current roadmap.
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
- Capability probing uses `IHardwareMediaCapabilityProbe.ProbeAsync`; never block the UI thread.


## Studio UI Exception

A limited Avalonia Studio UI/mock track is allowed when it follows `docs/UI_STUDIO_DESIGN.md`, `docs/UI_REACT_TO_AVALONIA_MAPPING.md`, `docs/UI_IMPLEMENTATION_PLAN.md`, and `docs/UI_ACCEPTANCE_CHECKLIST.md`.

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
