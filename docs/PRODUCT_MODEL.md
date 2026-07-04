# WTK MediaForge Product Model

This document is the product contract for WTK MediaForge. It describes the
authoring model users work with: projects, sources, canvases, draw objects,
effects, and outputs. GPU execution details stay behind runtime snapshots,
render threads, and backends.

If a new media feature does not fit this model, update this document before
adding code. Do not add source-specific draw objects, output-specific renderer
branches, or public GPU concepts to the product layer.

For GPU lifecycle, render thread, submission, registry, and backend contracts,
see [ARCHITECTURE.md](../ARCHITECTURE.md). For public API boundaries, see
[PUBLIC_API.md](PUBLIC_API.md).

## Current Status

| Area | Status | Notes |
|---|---|---|
| H1 product contract | Complete | Product/runtime separation is documented. |
| H2 source catalog/settings | Complete foundation | Typed source settings and serializers exist. Real providers beyond desktop remain blocked. |
| H3 output catalog/settings | Complete foundation | Typed output settings, serializers, public sink contracts, and CPU readback sample sink exist. Real product sinks remain blocked. |
| H4 effect model | Complete foundation | Effect types, ordering, snapshots, and validation exist. Renderer support is not implied. |
| H5 project editor | Complete foundation | `MediaForgeProjectEditor` is the supported mutation primitive. |
| H6 canvas graph validation | Complete foundation | Cycles and max nested depth 8 are validated. |
| H7 engine facade skeleton | Complete foundation | Engine exists and now has transactional update/bind/unbind and safer stop behavior. |
| Scene routing/packages | Complete foundation | Public `Scene` alias, route helpers, scene package export/import, presets, and dry-run validation exist. |
| Render graph planning | Complete foundation | Internal DAG planning deduplicates sources, reusable effect chains, canvases, and output passes. |

## Layer Boundary

| Concern | Product layer | Runtime/GPU layer |
|---|---|---|
| Editable state | `MediaForgeProject`, editor/builder APIs | Immutable snapshots only |
| Source definitions | `MediaForgeSourceDefinition` + typed settings | `IVideoFrameProvider` and GPU frame leases |
| Canvas graph | `MediaForgeCanvas`, draw objects, effects | `RenderCanvasSnapshot`, `RenderFrameSnapshot` |
| Outputs | `MediaForgeRenderOutput` + typed settings + public sink contracts | internal output binding, sink dispatcher, and `RenderOutputBindingSnapshot` |
| Rendering | layout, opacity, crop, effect intent | Vulkan pipelines, descriptor sets, framebuffers |
| Diagnostics | validation/runtime diagnostics | backend and lifecycle diagnostics |

Forbidden product-layer shortcuts:

- `WebcamDrawObject`, `NdiDrawObject`, `RtspDrawObject`: use `SourceLayerDrawObject` plus source definitions.
- NDI, encoder, stream, or file logic inside `MediaForgeVulkanRenderer`: use output type plus sink factory.
- Chroma key as a source-layer property: use the effect model.
- Public APIs that require `CompositionRuntime`, `MediaForgeRenderThread`, `IRenderBackendFactory`, Vulkan registry types, or GPU leases.

## Project

`MediaForgeProject` is the serializable root:

- `SourceDefinitions`
- `Canvases`
- `Outputs`
- schema/version metadata

It is valid storage API. Public callers should prefer `MediaForgeProjectBuilder`,
`MediaForgeProjectEditor`, typed helper factories, and package import/export
APIs for normal authoring.

## Sources

Sources are defined once and referenced by source layers.

Current public settings DTOs include:

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

`JsonObject` remains storage, migration, and validator infrastructure. It is not
the normal public authoring experience.

Real webcam, NDI, RTSP/IP camera, MP4 timeline, static image, animated image,
and Lottie providers remain planned integrations. Their project type contracts
exist before the runtime adapters are implemented.

## Draw Objects

Current draw object model:

- `SourceLayerDrawObject`
- `TextDrawObject`
- `SolidDrawObject`
- `CanvasDrawObject`

Common properties include transform, crop, opacity, blend mode, enabled state,
and ordered effects. Draw objects describe intent; they never own GPU resources.

## Canvases

Canvases hold ordered draw objects and may reference other canvases through
`CanvasDrawObject`.

Rules:

- cycles are invalid
- maximum nested canvas depth is 8
- nested canvas rendering is implemented in the Vulkan offscreen path
- a canvas can be routed to multiple outputs
- public `Scene` naming is an ergonomic alias over `MediaForgeCanvas`
- reusable canvases should be render-graph dedupe points when size/config/version match

## Routing And Render Graph

Outputs route to canvases. Public applications may treat those canvases as
scenes, preview scenes, program scenes, nested layouts, or reusable templates.

The target routing model is:

```text
Source -> SourceLayer(s) -> Canvas/Scene -> RenderOutput -> RenderOutputSink(s)
```

The first internal render-graph planner exists and compiles routed outputs into
stable nodes:

- source frame nodes
- reusable source effect-chain nodes
- canvas render nodes
- output pass nodes

This planner is currently a product/runtime foundation and test target. It is
not yet the Vulkan execution scheduler, but new renderer and sink work should
preserve the same dedupe contract: same source once, same reusable effect chain
once, same canvas once, then split only for output-specific fit/presentation.

## Effects

The effect model exists as a product contract and snapshot contract:

- `ChromaKeyEffect`
- `ColorCorrectionEffect`
- `BlurEffect`
- `TransitionEffect`

Effects may be valid project data before the renderer implements them. Public
docs and samples must not imply that an effect is rendered until renderer tests
prove it.

## Outputs

Current output settings DTOs include:

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

Current public output target contracts:

- `RenderOutputTarget`
- `OffscreenRenderOutputTarget`
- `WinFormsPreviewRenderOutputTarget`

Current public sink contracts:

- `CpuReadbackSink`
- `CpuReadbackFrame`
- `CpuReadbackFrameEventArgs`
- `IRenderOutputSink`
- `RenderOutputSinkId`
- `RenderOutputSinkKind`
- `RenderOutputSinkBackpressureMode`
- `RenderOutputFrameLease`
- `RenderOutputFrameInfo`
- `FrameNotificationSink`

The output architecture is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

The same `RenderOutput` can feed multiple sinks. `FrameNotificationSink` is the
first functional public sink for completed-frame metadata in diagnostics, tests,
and samples. It is not CPU readback and not the main GPU-first path for
production preview or encoding. `CpuReadbackSink` is the first public visual
sink and delivers owned CPU pixel buffers after a backend surface has completed.
`PreviewPanelSink` is the experimental GPU preview path. Productive preview
shells, NDI, MP4/encoded file, RTMP/SRT/RTSP/HLS streaming, virtual camera, and
audio outputs remain planned integrations until the active roadmap opens those
tracks.

## Package Serialization

Serializable package types exist for saving and exchanging product model state:

- `MediaForgeProject` for full save/load
- `MediaForgeScenePackage` for one scene/canvas with nested canvases, sources,
  routed outputs, effects, and metadata
- `MediaForgeCanvasPreset` for reusable layout/PiP/mosaic/canvas arrangements
- `MediaForgeSourcePreset` for source definitions without runtime state
- `MediaForgeOutputPreset` for output profiles with secret-safe export by default
- `MediaForgeEffectPreset` for reusable effect chains

Package JSON is schema-versioned product data. It must not contain runtime
leases, native handles, Vulkan/D3D11 objects, command buffers, fences, backend
worker state, sink queue state, or secrets unless explicitly requested through
export options.

Import modes are replace project, merge as new scene, merge presets only, and
dry-run validation. Import builds and validates a candidate project before
returning it to callers; failed import and dry-run modes must not mutate the
existing project.

## Engine

`MediaForgeEngine` is the runtime facade skeleton. Current hardening:

- `ApplyProjectUpdateAsync` edits a cloned project and swaps only after validation.
- `CurrentProject` returns a deep clone and cannot expose the engine-owned mutable project instance.
- `StartAsync` requires a loaded project and `StopAsync` returns to `Loaded` when the project remains loaded.
- `StartTimeout`, `CommandTimeout`, `StopTimeout`, and render pump frame rate are public Windows facade options.
- invalid project updates keep the engine project, project state snapshots, and frame publication intact.
- `BindOutputAsync` creates and validates the new sink/binding before swapping.
- bind failures keep the previous sink registered and dispose the failed new sink.
- `UnbindOutputAsync` enqueues unbind before disposing the sink and removes the engine registration even if disposal fails.
- `AttachSinkAsync` and `DetachSinkAsync` connect public sinks to an offscreen `RenderOutput` surface.
- a continuous internal render pump publishes frames while the engine is running.
- `StopAsync` does not dispose the backend when the render thread is still alive; it reports a fatal diagnostic and enters a failed internal state.
- if render-thread cleanup fails after the thread already stopped, the backend is still disposed.

These are PAPI-2 through PAPI-8.

## CP1 Renderer Status

CP1 is a hardened first Vulkan path, not the final compositor.

Now covered:

- source-layer framebuffer/descriptor lifetime until submission fence completion
- offscreen target retention until submission cleanup
- source import layout remains shader-read after submit
- queue-submit failure rolls back offscreen layouts
- descriptor sets are released after fence completion
- descriptor pool capacity is explicit, not the old magic 16
- registry waiters use timeout diagnostics instead of blocking forever
- source-layer Fit outputs transparent pixels outside content
- output letterbox pixels are verified by readback
- canvas background color is rendered
- layers partially outside the canvas are clipped and fully outside layers draw nothing
- center, fit, fill, stretch, opacity, letterbox, background, and clipping pixels have GPU tests

Implemented after CP1:

- CP2 multi-layer product compositor
- CP3 solid layers
- CP3 nested canvas rendering
- first effect rendering through `ChromaKeyEffect`
- experimental GPU preview through `PreviewPanelSink`

Still not implemented:

- productive preview shell
- text rendering
- blur/color correction/transitions
- encoder, NDI, webcam, RTSP/IP camera, MP4 timeline, animated image, Lottie, audio

## Readiness Checklist

| Capability | Product contract | Implementation |
|---|---|---|
| Project, canvas, draw objects | Yes | Yes |
| Source/output typed settings | Yes | Foundation complete |
| Editor API | Yes | Foundation complete |
| Engine facade | Yes | Public runtime foundation complete |
| CP1 offscreen Vulkan path | Yes | Hardened for first-source visual proof |
| CP2/CP3 compositor | Yes | Multi-layer, solid, nested, and chroma foundation complete |
| Scene routing/packages | Yes | Foundation complete |
| Render graph planning | Yes | Foundation complete |
| Public SDK experience | Yes | Initial authoring/runtime/sink/package path exists |

Verdict: the product model foundation and initial public runtime/sink path are
in good shape. The next work should stabilize PreviewPanelSink locally, then
advance renderer primitives and media adapters in the order defined by
`docs/ROADMAP_CURRENT.md` and `docs/FULL_PIPELINE_ROADMAP.md`.
