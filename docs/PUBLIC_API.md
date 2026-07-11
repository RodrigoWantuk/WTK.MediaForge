# Public API

WTK MediaForge exposes a product API for authoring, validating, loading, and operating media compositions. GPU, capture, render-thread, snapshot, and submission ownership details are implementation internals.

This document is the public API boundary. New public types must fit one of the public sections below. Runtime/GPU helper types must remain internal unless this document is updated first.

The Avalonia Studio application is a product shell over these APIs. Its
ViewModels, mock services, workspace editor state, dialogs, and visual controls
are not part of the engine/library public API unless explicitly listed here in a
future revision.

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
- `MediaForgeSources`
- `MediaForgeOutputs`
- `MediaForgeProjectLoader`
- `MediaForgeProjectSerializer`
- `MediaForgeProjectMigrator`
- `MediaForgeProjectValidator`
- `ProjectValidationResult`
- `MediaForgeProjectValidationException`
- `ValidationIssue`
- `OutputRouteTransition`
- `OutputRouteTransitionKind`
- `OutputRouteTransitionRuntime`

Applications should not directly construct render snapshots, runtime snapshots, GPU leases, render threads, or backend submissions.

`Scene(...)` is a public authoring alias over `MediaForgeCanvas`. Internally the
canonical render primitive remains canvas; the public API may use scene naming to
fit live-production workflows without introducing a second scene graph.

`MediaForgeSources` and `MediaForgeOutputs` are typed helper factories for
source/output definitions. They create serializable project definitions only;
they do not create capture devices, decoders, encoders, network clients, GPU
surfaces, or sink workers.

Current scoped rendering support includes:

- `TextDrawObject.FontFamily`, propagated through snapshots into the Vulkan text
  renderer.
- `TextLayerBuilder.SetFontFamily(...)` for fluent text authoring.
- `SourceLayerBuilder.AddBlur(...)` for the currently supported source-layer
  blur effect.
- `MediaForgeRenderOutput.RouteTransition` for cut/fade route transitions on an
  output.

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
- unavailable or unsupported features throw `MediaForgeUnsupportedFeatureException`
- `CurrentProject` returns a deep clone; public callers cannot mutate engine-owned project state directly
- `StartAsync` requires a loaded project and `StopAsync` returns to `Loaded` when that project remains loaded
- `StartTimeout`, `CommandTimeout`, `StopTimeout`, `SinkStopTimeout`, and `RenderFramesPerSecond` are applied by `MediaForgeWindows.CreateEngine`
- project updates and output bind/unbind operations are transactional
- render-thread command failures are reported to the caller
- a continuous internal render pump publishes frames while the engine is running
- shutdown skips backend disposal if the render thread is still alive
- sink detach/dispose uses an explicit timeout; a hung sink is reported instead of blocking engine shutdown indefinitely
- diagnostics, state changes, and frame drops are exposed as events

Applications must not manually wire `CompositionRuntime`, `MediaForgeRenderThread`, `RenderThreadGuard`, `IRenderBackendFactory`, `IRenderOutputSinkFactory`, or `IMediaSourceProviderFactory`.

## 3. Public Source Settings

Source settings are public typed DTOs. Public callers should use these types instead of `JsonObject`.

Current public source settings:

- `AnimatedImageSourceSettings`
- `DesktopCaptureSourceSettings`
- `WindowCaptureSourceSettings`
- `ImageFileSourceSettings`
- `IpCameraSourceSettings`
- `LottieSourceSettings`
- `VideoFileSourceSettings`
- `WebcamSourceSettings`
- `NdiInputSourceSettings`
- `RtspInputSourceSettings`
- `GeneratedSourceSettings`

`JsonObject` remains valid for serialization, migration, and validation of saved project files. It is not the normal public authoring API.

## 4. Public Output Settings

Output settings are public typed DTOs. Public callers should use these types instead of raw `JsonObject`.

Current public output settings:

- `EncodedFileOutputSettings`
- `OffscreenOutputSettings`
- `PreviewWindowOutputSettings`
- `RecordingMp4OutputSettings`
- `StreamingHlsOutputSettings`
- `StreamingRtspOutputSettings`
- `StreamingRtmpOutputSettings`
- `StreamingSrtOutputSettings`
- `VirtualCameraOutputSettings`
- `NdiOutputSettings`

Public output targets are typed separately from saved output settings.

Current public output target contracts:

- `RenderOutputTarget`
- `OffscreenRenderOutputTarget`
- `WinFormsPreviewRenderOutputTarget`

Current public sink contracts:

- `CpuReadbackFrame`
- `CpuReadbackFrameEventArgs`
- `CpuReadbackSink`
- `PreviewPanelSink`
- `IRenderOutputSink`
- `RenderOutputSinkId`
- `RenderOutputSinkKind`
- `RenderOutputSinkBackpressureMode`
- `RenderOutputSinkContext`
- `RenderOutputFrameLease`
- `RenderOutputFrameInfo`
- `RenderPixelFormat`
- `RenderBackendKind`
- `FrameNotificationSink`
- `FrameNotificationEventArgs`
- `EncodedVideoPacket`
- `EncodedVideoPacketLease`
- `EncodedVideoCodec`
- `EncodedVideoBitstreamFormat`

The product architecture is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

`AttachSinkAsync` / `DetachSinkAsync` is the public direction for consuming completed output frames. `BindOutputAsync` remains for the internal/legacy target bridge while the sink model is completed. `FrameNotificationSink` is intended for diagnostics, samples, and tests that need completed-frame notification metadata. It does not expose pixels and must not be treated as CPU readback. `CpuReadbackSink` is a debug/sample/validation sink only (`MediaTransportKind.DebugOnlyCpuReadback`): it copies pixels into an owned CPU buffer and must not become the primary preview, encoder, or streaming path. Product recording and streaming sinks consume `EncodedVideoPacket` after hardware encode only. Encoded packets must identify codec, bitstream format (`AnnexB` or `Avcc` for H.264), presentation time, optional duration, and optional codec configuration data; sinks must reject unknown bitstream format instead of guessing. `PreviewPanelSink` is an **experimental** GPU preview sink: it consumes a completed rendered output surface and presents it to a Win32 panel handle through an internal Vulkan swapchain blit, without CPU readback. Keep it experimental until the preview local reliability milestone in `docs/ROADMAP_CURRENT.md` is complete.

FFmpeg is not used in the first hardware MP4/RTMP product path. Future FFmpeg integration requires LGPL-only build, no GPL components, no libx264/libx265, no rawvideo pipe, and license review.

## 4.1 Public Capability API

Capability and license status are queryable without starting the engine:

- `MediaForgeWindows.GetCapabilityReportAsync(CancellationToken)` - must not block the UI thread; probing runs via `IHardwareMediaCapabilityProbe.ProbeAsync`.
- `MediaForgeCapabilityReport`, `CapabilityEntry`, `MediaForgeSupportStatus`, `MediaForgeLicenseStatus`, `MediaForgeProductReadinessStatus`
- `HardwareMediaBackendCapability` reports runtime-detected OS/vendor backend facts for hardware decode/encode. A backend that requires CPU staging for continuous video, or is only `Prototype`/`Skeleton`, must not be reported as `Supported` or `Experimental`.
- `CapabilityEntry.ProductReadinessStatus` separates contract/prototype/skeleton/backend-call/product-validated evidence from user-facing support status. `Prototype` and `Skeleton` entries must never be `Supported` or `Experimental`.
- Capability entries that are not user-available (`PrototypeOnly`, `Planned`, `Unsupported`, `Blocked`, `Prohibited`, or equivalent non-product states) must include a non-empty `UnavailableReason` suitable for UI and diagnostics.
- `MediaTransportAuditEvent.EvidenceKind` and `MediaTransportAuditEvidenceKind` distinguish contract-only, prototype, backend-call, and backend-output-validated evidence.
- `IHardwareFileVideoDecoder` and `FileDecodeFrameContext` represent file decoders that own demux/decode internally; file-video runtimes must not pass empty packets into packet decoders.
- `IStaticImageAssetDecoder`, `StaticCpuAsset`, and `StaticImageAssetFormats` define load-time static image decode contracts. Platform assemblies provide decoder implementations; Composition does not own `System.Drawing` or any platform image decoder. On Windows, `MediaForgeWindows.CreateEngine()` wires PNG/JPEG image sources through load-time decode, D3D11 shared texture upload, and GPU frame leases; provider wiring remains internal.

Studio and host apps must use capability status to disable or label features that are PrototypeOnly, Planned, Unsupported, or Blocked.

Productive preview shells, NDI, MP4/encoded file, streaming, virtual camera, and audio outputs remain blocked until the owning roadmap track opens and capability report reflects Supported status.

Sink compliance metadata follows the same rule: sinks that are debug-only,
prototype-only, planned, unsupported, blocked, or otherwise not product-ready
must expose a user-visible reason instead of relying on silent disablement.

## 5. Public Package And Preset Serialization

The public package surface supports save/load, scene export/import, and reusable
presets without exposing runtime resources.

Current public package types:

- `MediaForgePackageExportOptions`
- `MediaForgePackageSerializer`
- `MediaForgeProjectPackages`
- `MediaForgeProjectImportMode`
- `MediaForgeProjectImportResult`
- `MediaForgeScenePackage`
- `MediaForgeCanvasPreset`
- `MediaForgeSourcePreset`
- `MediaForgeOutputPreset`
- `MediaForgeEffectPreset`

Package JSON can contain stable ids, schema version, type ids, typed settings,
transforms, effects, canvas graph, output routes, and metadata. Package JSON
must not contain runtime leases, native handles, Vulkan/D3D11 resources, command
buffers, fences, backend worker state, sink queues, or secret credentials unless
export options explicitly allow secrets.

Supported import modes are replace project, merge as new scene, merge presets
only, and dry-run validation. Import validates by building a candidate project
first; it must not mutate the caller's project when validation fails or when the
mode is dry-run.

## 6. Public Effect Model

Effects are public project-level authoring types.

Current public effects:

- `MediaForgeEffect`
- `ChromaKeyEffect`
- `ColorCorrectionEffect`
- `BlurEffect`
- `TransitionEffect`

Effects may exist as project model contracts before the renderer implements every effect. Public API must not imply that an effect is rendered unless renderer support and tests exist.

Current renderer-backed effect support:

- `ChromaKeyEffect` on source layers.
- `ColorCorrectionEffect` on source layers in the Vulkan shader path.
- `BlurEffect` on source layers in the Vulkan intermediate-pass path.

Scene-wide effects, transition effects as generic layer/scene effects, and
non-source-layer blur remain project contracts until their owning renderer
scope lands.

## 7. Public Diagnostics

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

## 8. Advanced Low-Level API

The primary SDK path does not require D3D11, Vulkan, GPU slot, or native handle
types. Some low-level assemblies still expose advanced types for diagnostics,
interop, and implementation-level tests. Those types are explicitly frozen by
`Public_api_matches_approved_allowlist`.

Advanced public types are allowed only when they are intentionally listed in the
public API allowlist test. New low-level public types must update this document
and the allowlist in the same change.

## 9. Internal GPU/Runtime APIs

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

## 10. Prohibited Public Exposure

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
