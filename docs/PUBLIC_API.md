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
- `SceneEditMode`
- `SceneEditSessionId`
- `SceneVersionId`
- `SceneVersionBinding`
- `SceneVersionBindingKind`
- `SceneMutationPatch`
- `SceneEditSessionDescriptor`
- `SceneDraftState`
- `ScenePublishedState`
- `SceneApplyTransitionPolicy`
- `SceneApplyTransitionKind`
- `SceneCommitRequest`
- `SceneCommitResult`

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
- `WindowsNdiDiscoveryOptions`
- `WindowsNdiSourceInfo`
- `MediaForgeEngineOptions`
- `MediaForgeEngineState`
- `MediaForgeEngineException`
- `MediaForgeUnsupportedFeatureException`
- `MediaForgeDiagnosticEventArgs`
- `MediaForgeEngineStateChangedEventArgs`
- `MediaForgeFrameDroppedEventArgs`
- `EncodedOutputRuntimeStatus`
- `EncodedOutputRuntimeSnapshot`

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
- `GetEncodedOutputRuntimeSnapshots()` exposes high-level encoded output state
  (`Running`, `Backpressure`, `Failed`, etc.) and counters without exposing
  encoder workers, GPU surfaces, command buffers, or native handles
- `BeginSceneEditSessionAsync(...)`, `ApplySceneMutationAsync(...)`,
  `ApplySceneDraftAsync(...)`, and `DiscardSceneDraftAsync(...)` are the public
  scene-editing APIs. `Live` sessions update the published scene after
  validation; `Apply` sessions mutate an isolated draft until commit. Drafts do
  not affect published sinks before `ApplySceneDraftAsync`.
- `MediaForgeWindows.FindNdiSourcesAsync(...)` performs Standard NDI SDK source
  discovery on Windows when a licensed/loadable runtime is present. It returns
  names and addresses only; it does not receive video frames, allocate raw
  frame buffers, or promote NDI video input/output to product support.

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
- `EncodedVideoProfile`
- `H264Profile`
- `H264Level`
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
- `EncodedVideoPacketEvidence`
- `EncodedVideoPacketLease`
- `EncodedVideoCodec`
- `EncodedVideoBitstreamFormat`
- `EncodedOutputRuntimeStatus`
- `EncodedOutputRuntimeSnapshot`

The product architecture is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

`AttachSinkAsync` / `DetachSinkAsync` is the public direction for consuming completed output frames. `BindOutputAsync` remains for the internal target bridge while the sink model is completed. `FrameNotificationSink` is intended for diagnostics, samples, and tests that need completed-frame notification metadata. It does not expose pixels and must not be treated as CPU readback. `CpuReadbackSink` is a debug/sample/validation sink only (`MediaTransportKind.DebugOnlyCpuReadback`): it copies pixels into an owned CPU buffer and must not become the primary preview, encoder, or streaming path. Product recording and streaming sinks consume `EncodedVideoPacket` after hardware encode only. Encoded packets identify codec, bitstream format, presentation time, duration, codec configuration, and trusted evidence. MP4 and RTMP reject non-validated or unknown bitstreams. `PreviewPanelSink` is an experimental Win32/Vulkan GPU sink: it preserves in-flight presenter resources after timeout and must not introduce CPU readback, but remains unpromoted until hosted reliability evidence passes.

FFmpeg is not used in the first hardware MP4/RTMP product path. Future FFmpeg integration requires LGPL-only build, no GPL components, no libx264/libx265, no rawvideo pipe, and license review.

## 4.1 Public Capability API

Capability and license status are queryable without starting the engine:

- `MediaForgeWindows.GetCapabilityReportAsync(CancellationToken)` - must not block the UI thread; probing runs via `IHardwareMediaCapabilityProbe.ProbeAsync`.
- `MediaForgeWindows.CreateHardwareMediaProofRegistry()` and `MediaForgeWindows.GetCapabilityReportWithHardwareProofsAsync(...)` are the explicit Windows entrypoints for running local hardware proof runners and merging observed results into a capability report. They may touch D3D11/Media Foundation hardware and must not be hidden inside cheap UI-thread probes.
- `MediaForgeCapabilityReport`, `CapabilityEntry`, `MediaForgeSupportStatus`, `MediaForgeLicenseStatus`, `MediaForgeProductReadinessStatus`
- `HardwareMediaValidationReport`, `HardwareMediaValidationFeature`,
  `HardwareMediaValidationProof`, `HardwareMediaValidationCapability`,
  `HardwareMediaValidationStatus`,
  `HardwareMediaValidationReportBuilder`, and
  `HardwareMediaValidationReportMarkdownWriter` provide the versioned
  operational report used by readiness scripts and release validation. The
  current schema version is `1`. Window Capture has its own
  `proof.media_io.window_capture.product` chain and is not inferred from nominal
  Windows API availability.
- `CapabilityProofAggregator` resolves composite product capabilities such as
  MP4 recording, RTMP streaming, and MP4 video input from required hardware
  media proof results. The Windows capability report also promotes webcam and
  window capture input when their product proofs pass. Features must not be promoted manually or from
  prototype evidence.
- `MediaForgeCapabilityCatalog.NdiSourceDiscovery` reports whether the Windows
  Standard NDI SDK runtime can perform source discovery. This capability is
  metadata/discovery only and must not be used as proof that NDI video
  input/output is available.
- `HardwareMediaBackendCapability` reports runtime-detected OS/vendor backend facts for hardware decode/encode. A backend that requires CPU staging for continuous video, or is only `Prototype`/`Skeleton`, must not be reported as `Supported` or `Experimental`.
- `HardwareMediaProof` and `HardwareMediaProofStatus` report concrete v14 proof results for render-to-encode, hardware encode, MP4 recording, hardware decode, decode-to-render, MP4 input/output, webcam input, window capture input, RTMP network output, and NDI input/output. `HardwareMediaProofRegistry` executes proof runners once per cached adapter/device generation. Non-passed proofs require a user-visible reason; passed packet/media proofs require trusted backend evidence.
- `MediaForgeHardwareAdapterInfo` and `MediaForgeCapabilitySnapshot` expose immutable adapter identity, driver/device generation, capture time, and the capability report. Hosts call asynchronous platform probes; they must not block a UI thread.
- `MediaForgeRuntimeHealthSnapshot` and recovery events expose high-level
  engine/output/source health without leaking Vulkan or D3D11 details.
  `SceneVersions` reports bounded scene-history retention, active pins,
  discarded versions, and the observed high-water mark. These are aggregate
  ownership counters; internal resolved keys and draft contents are not public.
  `GpuResources` reports aggregate submission, import, target, pool,
  framebuffer, descriptor-set, retired-resource, and high-water counters. It
  exposes no native handles and is intended for health monitoring and sustained
  baseline-return validation.
- `CapabilityEntry.ProductReadinessStatus` separates contract/prototype/skeleton/backend-call/product-validated evidence from user-facing support status. `Prototype` and `Skeleton` entries must never be `Supported` or `Experimental`.
- Capability entries that are not user-available (`Unavailable`, `PrototypeOnly`, `InternalOnly`, `Planned`, `Deferred`, `Unsupported`, `Blocked`, `Prohibited`, or equivalent non-product states) must include a non-empty `UnavailableReason` suitable for UI and diagnostics.
- `MediaTransportAuditEvent.EvidenceKind` and `MediaTransportAuditEvidenceKind` distinguish contract-only, prototype, backend-call, and backend-output-validated evidence.
- `IHardwareVideoEncoder`, `HardwareVideoEncoderSettings`, and `EncodeFrameContext` represent hardware-only encoder input. Settings validate codec, dimensions, FPS, bitrate, keyframe interval, and GPU input format before a platform encoder session starts.
- `EncodedVideoProfile` is the public, serializable profile attached to MP4 and
  RTMP outputs. `H264Profile` and `H264Level` are validated enums; project JSON
  retains canonical string values such as `"High"` and `"4.2"` for backward
  compatibility. Platform route factories map the profile to
  `HardwareVideoEncoderSettings`; encoder FPS, bitrate, GOP, profile/level, and
  pixel format must not be hardcoded in the Windows route. `HardwareEncoderInfo`
  exposes requested and negotiated H.264 values after backend initialization.
- `IHardwareFileVideoDecoder` and `FileDecodeFrameContext` represent file decoders that own demux/decode internally; file-video runtimes must not pass empty packets into packet decoders.
- Product file decode must return GPU-backed frames; system-memory decoded samples are unavailable, not a fallback.
- `IStaticImageAssetDecoder`, `StaticCpuAsset`, and `StaticImageAssetFormats` define load-time static image decode contracts. Platform assemblies provide decoder implementations; Composition does not own `System.Drawing` or any platform image decoder. On Windows, `MediaForgeWindows.CreateEngine()` wires PNG/JPEG image sources through load-time decode, D3D11 shared texture upload, and GPU frame leases; provider wiring remains internal.

Studio and host apps must use capability status to disable or label features that are `Unavailable`, `PrototypeOnly`, `InternalOnly`, `Planned`, `Deferred`, `Unsupported`, or `Blocked`.

Remote Scene connection and ICE options are runtime configuration, not project
secrets. `MediaForgeProject` may persist only a safe connection-profile
reference. Signaling bearer tokens, invitation codes, TURN credentials, SDP,
and ICE candidates must remain outside project JSON. The separately deployed
`WTK.MediaForge.Remote.Signaling` service coordinates authenticated peers but
does not expose a media source or sink capability. Remote publish/subscribe APIs
remain unavailable until the native encoded-access-unit bridge and composite
GPU proofs pass.

`MediaForgeRenderOutput.Enabled` is canonical project state. Disabled outputs
remain serializable and editable but are excluded from dependency routing,
render snapshots, surface bindings, encoded groups, and recovery. Hosts must
enable an output explicitly before attaching a sink or starting its route.

Studio persistence is a projection over `MediaForgeProject`, not a competing
document format. `StudioProjectSession` owns the canonical clone and applies
edits by stable ids. Fields not represented by current ViewModels remain intact.
Save produces and validates a detached snapshot, writes a temporary file in the
destination directory, atomically replaces the destination, and only then
advances the in-memory canonical session.

Operational validation scripts use
`./scripts/generate-media-proof-report.ps1` and
`./scripts/verify-engine-readiness-v14.ps1` to write
`test-reports/media-proof-report.json` and
`test-reports/media-proof-report.md`. In normal development, unavailable
hardware paths are allowed only when the report contains explicit blockers. In
release mode, `-RequireHardwareMedia` fails unless required hardware media
features have passed proof chains.

Preview panel sinks are experimental GPU-surface sinks for Win32/Vulkan. Fence timeout preserves presenter resources for retry; product promotion still requires hosted resize/attach/detach and sustained presentation evidence. MP4 recording, RTMP streaming, MP4 input, webcam input, and window capture are enabled only through the current adapter capability snapshot. Window Capture accepts an HWND and publishes only engine-owned D3D11 GPU leases; the WinRT frame-pool surface never escapes its frame lifetime. NDI runtime detection/discovery does not make NDI video available. SRT, virtual camera, and audio remain unavailable/planned.

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
