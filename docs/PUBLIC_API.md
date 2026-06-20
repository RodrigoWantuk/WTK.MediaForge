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
- `MediaForgeProjectLoader`
- `MediaForgeProjectSerializer`
- `MediaForgeProjectMigrator`
- `MediaForgeProjectValidator`
- `ProjectValidationResult`
- `ValidationIssue`

Planned public authoring type:

- `MediaForgeProjectBuilder`

Applications should not directly construct render snapshots, runtime snapshots, GPU leases, render threads, or backend submissions.

## 2. Public Runtime API

The public runtime surface is the supported way to operate a composition.

Current public runtime type:

- `MediaForgeEngine`

The current engine remains an early facade. The product-level creation facade is not complete yet.

Planned public runtime types:

- `MediaForgeWindows`
- `MediaForgeEngineOptions`
- `MediaForgeEngineState`
- `MediaForgeEngineException`

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

Only offscreen output is expected to become functional first. Real preview, NDI, MP4, streaming, and audio outputs remain blocked until the public runtime API is stable.

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

Planned public diagnostics:

- `MediaForgeDiagnosticEventArgs`
- `MediaForgeEngineStateChangedEventArgs`
- `MediaForgeFrameDroppedEventArgs`

## 7. Internal GPU/Runtime APIs

The following categories are internal implementation details:

- Render thread and pending submission ownership
- Runtime source/provider wiring
- Runtime output sink wiring
- Render backend and submission interfaces
- Project state snapshots
- Render frame snapshots
- Snapshot factories
- GPU frame leases
- GPU slot rings and D3D11 slot rings
- Vulkan renderer implementation
- Vulkan external texture registry
- Vulkan imported texture/image-view resources
- Shader pipeline catalog and render draw-object pipeline mapping
- Test/fake frame sources and handles

These types may be visible to test assemblies or implementation assemblies through `InternalsVisibleTo`; that does not make them product API.

## 8. Prohibited Public Exposure

The following types must not be public:

- `CompositionRuntime`
- `MediaForgeRenderThread`
- `PendingRenderSubmissionTracker`
- `LatestSnapshotBuffer`
- `RenderThreadGuard`
- `IRenderBackend`
- `IRenderBackendFactory`
- `IRenderFrameSubmission`
- `IRenderOutputSink`
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
- `VulkanExternalTextureRegistry`
- `VulkanD3D11TextureImport`

Future work must proceed in this order:

1. `PAPI-2`: create `MediaForgeWindows.CreateEngine`.
2. `PAPI-3`: create `MediaForgeProjectBuilder`.
3. `PAPI-4`: complete public engine state/runtime API.
4. `PAPI-5`: finish typed render output targets.
5. `PAPI-6`: add public validation/runtime exceptions.
6. `PAPI-7`: add public engine events.
7. `PAPI-8`: add and build the offscreen sample.

NDI, encoder, streaming, MP4, webcam, RTSP, productive WinForms preview, UI designer, and plugin APIs remain blocked until the PAPI track is complete.
