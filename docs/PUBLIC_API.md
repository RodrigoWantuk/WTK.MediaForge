# Public API

WTK MediaForge exposes a product API for authoring, validating, loading, and operating media compositions. GPU, capture, render-thread, snapshot, and submission ownership details are implementation internals.

This document is the public API boundary for the PAPI stabilization track. New public types must fit one of the public sections below. Runtime/GPU helper types must remain internal unless this document is updated first.

## 1. Public Authoring API

The public authoring surface is the supported way for applications and tools to create or edit projects.

Current public authoring types:

- `MediaForgeProject`
- `MediaForgeCanvas`
- `MediaForgeSourceDefinition`
- `MediaForgeRenderOutput`
- `MediaForgeDrawObject`
- `SourceLayerDrawObject`
- `TextDrawObject`
- `SolidDrawObject`
- `CanvasDrawObject`
- `MediaForgeEffect`
- `MediaForgeProjectEditor`
- `MediaForgeProjectBuilder`
- `SourceLayerBuilder`
- `TextLayerBuilder`
- `CanvasLayerBuilder`
- `MediaForgeProjectLoader`
- `MediaForgeProjectSerializer`
- `MediaForgeProjectMigrator`
- `MediaForgeProjectValidator`
- `ProjectValidationResult`
- `MediaForgeProjectValidationException`
- `ValidationIssue`

Applications should not directly construct render snapshots, runtime snapshots, GPU leases, render threads, or backend submissions.

## 2. Public Runtime API

The public runtime surface is the supported way to operate a composition.

Current public runtime types:

- `MediaForgeEngine`
- `MediaForgeWindows`
- `MediaForgeEngineOptions`
- `MediaForgeEngineState`
- `MediaForgeEngineException`
- `MediaForgeUnsupportedFeatureException`
- `MediaForgeDiagnosticEventArgs`
- `MediaForgeEngineStateChangedEventArgs`
- `MediaForgeFrameDroppedEventArgs`

The engine API now has product-level public entry through `MediaForgeWindows.CreateEngine`.
Engine operations are observable:

- project validation failures throw `MediaForgeProjectValidationException`
- lifecycle/runtime failures throw `MediaForgeEngineException`
- unsupported planned features throw `MediaForgeUnsupportedFeatureException`
- `CurrentProject` returns a deep clone; public callers cannot mutate engine-owned project state directly
- `StartAsync` requires a loaded project and `StopAsync` returns to `Loaded` when that project remains loaded
- `StartTimeout`, `CommandTimeout`, `StopTimeout`, and `RenderFramesPerSecond` are applied by `MediaForgeWindows.CreateEngine`
- project updates and output bind/unbind operations are transactional
- render-thread command failures are reported to the caller
- a continuous internal render pump publishes frames while the engine is running
- shutdown skips backend disposal if the render thread is still alive
- diagnostics, state changes, and frame drops are exposed as events

Applications must not manually wire `CompositionRuntime`, `MediaForgeRenderThread`, `RenderThreadGuard`, `IRenderBackendFactory`, `IRenderOutputSinkFactory`, or `IMediaSourceProviderFactory`.

## 3. Public Source Settings

Source settings are public typed DTOs. Public callers should use these types instead of `JsonObject`.

Current public source settings:

- `DesktopCaptureSourceSettings`
- `WindowCaptureSourceSettings`
- `ImageFileSourceSettings`
- `VideoFileSourceSettings`
- `WebcamSourceSettings`
- `NdiInputSourceSettings`
- `RtspInputSourceSettings`
- `GeneratedSourceSettings`

`JsonObject` remains valid for serialization, migration, and validation of saved project files. It is not the normal public authoring API.

## 4. Public Output Settings

Output settings are public typed DTOs. Public callers should use these types instead of raw `JsonObject`.

Current public output settings:

- `OffscreenOutputSettings`
- `PreviewWindowOutputSettings`
- `RecordingMp4OutputSettings`
- `StreamingRtmpOutputSettings`
- `VirtualCameraOutputSettings`
- `NdiOutputSettings`

Public output targets are typed separately from saved output settings.

Current public output target contracts:

- `RenderOutputTarget`
- `OffscreenRenderOutputTarget`
- `WinFormsPreviewRenderOutputTarget`

Current public sink contracts:

- `IRenderOutputSink`
- `RenderOutputSinkId`
- `RenderOutputSinkKind`
- `RenderOutputSinkBackpressureMode`
- `RenderOutputSinkContext`
- `RenderOutputFrameLease`
- `RenderOutputFrameInfo`
- `RenderPixelFormat`
- `RenderBackendKind`
- `CpuReadbackSink`
- `CpuReadbackFrameEventArgs`

The product architecture is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

`AttachSinkAsync` / `DetachSinkAsync` is the public direction for consuming output frames. `BindOutputAsync` remains for the internal/legacy target bridge while the sink model is completed. `CpuReadbackSink` is intended for diagnostics, samples, and tests; it is not the main high-performance product path for preview, encoding, NDI, or streaming.

Real preview, NDI, MP4, streaming, and audio outputs remain blocked until the renderer composition track is stable.

## 5. Public Effect Model

Effects are public project-level authoring types.

Current public effects:

- `MediaForgeEffect`
- `ChromaKeyEffect`
- `ColorCorrectionEffect`
- `BlurEffect`
- `TransitionEffect`

Effects may exist as project model contracts before the renderer implements every effect. Public API must not imply that an effect is rendered unless renderer support and tests exist.

## 6. Public Diagnostics

Diagnostics are public so host applications can observe failures and warnings without depending on internal logs.

Current public diagnostics:

- `IMediaForgeDiagnosticsSink`
- `MediaForgeDiagnostic`
- `MediaForgeDiagnosticSeverity`
- `MediaForgeDiagnostics`
- `MediaForgeDiagnosticFactory`
- `NullDiagnosticsSink`
- `ListDiagnosticsSink`
- `InMemoryDiagnosticsSink`

## 7. Advanced Low-Level API

The primary SDK path does not require D3D11, Vulkan, GPU slot, or native handle
types. Some low-level assemblies still expose advanced types for diagnostics,
interop, and implementation-level tests. Those types are explicitly frozen by
`Public_api_matches_approved_allowlist`.

Advanced public types are allowed only when they are intentionally listed in the
public API allowlist test. New low-level public types must update this document
and the allowlist in the same change.

## 8. Internal GPU/Runtime APIs

The following categories are internal implementation details:

- Render thread and pending submission ownership
- Runtime source/provider wiring
- Runtime output binding and sink dispatcher wiring
- Render backend and submission interfaces
- Project state snapshots
- Render frame snapshots
- Snapshot factories
- GPU frame leases
- D3D11 physical slot-ring implementation
- Vulkan renderer implementation
- Vulkan external texture registry
- Vulkan imported texture/image-view resources
- Shader pipeline catalog and render draw-object pipeline mapping
- Test/fake frame sources and handles

These types may be visible to test assemblies or implementation assemblies through `InternalsVisibleTo`; that does not make them product API.

## 9. Prohibited Public Exposure

The following types must not be public:

- `CompositionRuntime`
- `MediaForgeRenderThread`
- `PendingRenderSubmissionTracker`
- `LatestSnapshotBuffer`
- `RenderThreadGuard`
- `IRenderBackend`
- `IRenderBackendFactory`
- `IRenderFrameSubmission`
- `WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink`
- `IRenderOutputSinkFactory`
- `IMediaSourceProviderFactory`
- `RenderFrameSnapshot`
- `RenderFrameSnapshotFactory`
- `ProjectStateSnapshot`
- `ProjectStateSnapshotFactory`
- `SnapshotBuildResult`
- `GpuFrameLease`
- `D3D11GpuFrameSlot`
- `D3D11GpuFrameSlotRing`
- `DesktopDuplicationFrameProvider`
- `MediaForgeVulkanRenderer`
- `MediaForgeVulkanRenderBackendFactory`
- `VulkanSmokeTest`
- `VulkanExternalTextureRegistry`
- `VulkanD3D11TextureImport`

PAPI status:

| # | Work | Status |
|---|---|---|
| PAPI-1 | Public API audit | Complete |
| PAPI-2 | `MediaForgeWindows.CreateEngine` | Complete |
| PAPI-3 | `MediaForgeProjectBuilder` | Complete |
| PAPI-4 | Public engine state/runtime API | Complete |
| PAPI-5 | Typed render output targets | Complete foundation |
| PAPI-6 | Public validation/runtime exceptions | Complete |
| PAPI-7 | Public engine events | Complete |
| PAPI-8 | Offscreen sample | Complete |

Current post-PAPI status:

- Public runtime ownership and engine state are hardened.
- A continuous render pump exists.
- Public render output sinks exist with bounded asynchronous dispatch.
- CPU readback sink proves public frame delivery for samples/tests.
- CP1 now honors canvas background and clips source layers to the canvas.

NDI, encoder, streaming, MP4, webcam, RTSP, productive WinForms preview, UI designer, and plugin APIs remain blocked until the renderer composition track is explicitly resumed.
