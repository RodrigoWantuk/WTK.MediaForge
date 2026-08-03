# Contributing to WTK MediaForge

By contributing to this project, you agree that your contribution may be distributed under the PolyForm Noncommercial License 1.0.0 and may also be used by the project owner under separate commercial licensing terms.

Read [`docs/README.md`](docs/README.md) before making architectural or product changes.

## Contribution principles

Contributions must preserve:

- the final product model rather than a temporary shortcut;
- GPU-first continuous-video transport;
- explicit ownership and bounded waits;
- truthful capability reporting;
- portable/platform dependency direction;
- deterministic failure and shutdown behavior;
- tests that prove the contract;
- documentation that reflects the implementation that actually exists.

Do not make a build pass by excluding a relevant project, suppressing a test, weakening a guard, adding a silent fallback, or misclassifying platform code.

## Work-unit scope

Keep each implementation unit focused and reviewable.

A contribution should have:

- one clear product or architecture objective;
- the matching implementation;
- focused tests;
- documentation updates when behavior, support status, public API, ownership, or validation changes;
- explicit validation evidence.

Avoid combining unrelated cleanup, feature work, architecture changes, UI redesign, and documentation rewrites in the same unit.

## Mandatory cross-platform contract

Windows and Linux are mandatory development targets.

Every contribution must:

- keep portable behavior independent from operating-system implementation;
- keep native APIs and handles inside the matching platform project;
- prevent portable projects from referencing Windows, Linux, or macOS implementation projects;
- cover portable behavior with tests that compile and run on Windows and Linux;
- add dedicated platform tests when behavior legitimately depends on native APIs;
- add new portable projects and portable test projects to the Linux lists in `.github/workflows/ci.yml`.

A contribution is not complete until both automatic jobs pass:

- `Windows build and tests`;
- `Linux build and tests`.

Linux portable success does not imply Linux physical media availability. Windows hardware success does not replace Linux portable validation.

## Media and GPU rules

Continuous uncompressed video must remain on GPU-backed surfaces on product paths.

Do not add:

- software video decode or encode fallback;
- raw-video pipes;
- primary preview through CPU readback;
- source-specific draw-object classes;
- renderer-triggered sinks;
- unbounded queues or waits;
- native handles as logical identity;
- prototype or skipped-test results reported as supported capability.

`CpuReadbackSink` is for debug, tests, and samples only.

Hardware-dependent capability must include a concrete unavailable reason when proof is missing or the active environment cannot provide it.

## Public API and product model

- `MediaForgeProject` remains the sole persisted product root.
- `MediaForgeCanvas` remains the canonical scene object.
- Sources remain reusable definitions referenced by layers.
- Live and Apply semantics remain engine behavior.
- Canvas-as-source remains versioned, cycle-safe, depth-bounded, and dependency-aware.
- Transitions remain output-route behavior.
- Public callers do not manually wire internal renderers, providers, encoders, exporters, or sink workers.

Changes to public contracts must update `docs/PUBLIC_API.md` and the applicable model/support documentation.

## Studio contributions

Studio is native Avalonia/MVVM.

Do not introduce React, WebView, Electron, browser runtime, or a competing persisted project format.

Production Studio behavior must use real engine and capability services. Design/test services must remain explicit and must not become runtime fallback.

Hosted preview must use GPU output leases and a platform presenter; the Avalonia overlay remains independent.

## Audio contributions

Portable audio work must preserve the real-time contract:

- no allocation, blocking, waiting, contended locks, disk access, formatted logging, UI invocation, or slow sink calls on the callback path;
- immutable compiled-plan publication between blocks;
- bounded pooled buffers and route-local pressure handling;
- native capture/playback in platform adapters only.

Portable mixing is implemented, but physical audio availability must remain unavailable until adapters and proofs exist.

## Required validation

### Baseline Windows validation

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release `
  --filter "Category!=GPU&Category!=Stress&Category!=Performance"
.\scripts\test.ps1 -Tier Fast
.\scripts\verify-media-transport-rules.ps1
.\scripts\verify-license-policy.ps1
```

### Linux validation

Use the authoritative project and test lists in `.github/workflows/ci.yml` with locked restore. Portable test assemblies must execute on both operating-system runners.

### GPU-sensitive changes

Changes to capture, D3D11, Vulkan, external memory, keyed mutex, preview presentation, GPU lifecycle, render thread, provider lifecycle, submission, hardware decode/encode, or GPU export require:

```powershell
.\scripts\test.ps1 -Tier Gpu
```

### Hardware-media promotion

```powershell
.\scripts\verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

### Studio changes

```powershell
.\scripts\verify-studio-ui-visual-qa.ps1
```

### Aggregate release gate

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia
```

Do not state that a gate passed unless it was executed and its report was inspected.

## Codec and dependency policy

Before adding or changing codec, container, or network-media dependencies, read:

- `docs/MEDIA_LICENSE_POLICY.md`;
- `docs/GPU_MEDIA_SUPPORT_MATRIX.md`;
- `docs/ROADMAP_CURRENT.md`.

FFmpeg/libav remains deferred to an encoded-packet/container-only legal and architecture review. The native MP4/RTMP product path must not depend on it.

Do not introduce GPL/nonfree builds, libx264, libx265, external codec executables, software media fallback, or rawvideo transport.

## Completion report

A completed contribution should state:

- files and behavior changed;
- tests and gates executed;
- reports inspected;
- validation not executed and why;
- remaining blockers or proof requirements;
- capability status changes, if any.
