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
| H7 engine facade skeleton | Complete foundation | Engine exists and now has transactional update/bind/unbind and safer stop behavior. Public API ergonomics are still PAPI work. |

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

It is valid storage API. It is not the final ergonomic authoring API for users.
Public callers should move toward `MediaForgeProjectBuilder` and
`MediaForgeProjectEditor` once PAPI-3 is implemented.

## Sources

Sources are defined once and referenced by source layers.

Current public settings DTOs include:

- `DesktopCaptureSourceSettings`
- `WindowCaptureSourceSettings`
- `ImageFileSourceSettings`
- `VideoFileSourceSettings`
- `WebcamSourceSettings`
- `NdiInputSourceSettings`
- `RtspInputSourceSettings`
- `GeneratedSourceSettings`

`JsonObject` remains storage, migration, and validator infrastructure. It is not
the normal public authoring experience.

Real webcam, NDI, RTSP, MP4, and image/video providers are still blocked until
the public API track is complete.

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
- nested canvas rendering remains CP3 work
- CP2 multi-layer rendering must not start until PAPI work is complete or the roadmap explicitly changes

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

- `OffscreenOutputSettings`
- `PreviewWindowOutputSettings`
- `RecordingMp4OutputSettings`
- `StreamingRtmpOutputSettings`
- `VirtualCameraOutputSettings`
- `NdiOutputSettings`

Current public output target contracts:

- `RenderOutputTarget`
- `OffscreenRenderOutputTarget`
- `WinFormsPreviewRenderOutputTarget`

Current public sink contracts:

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
production preview or encoding. Productive preview, NDI, MP4, streaming,
virtual camera, and audio outputs remain blocked.

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

Still not implemented:

- CP2 multi-layer product compositor
- CP3 nested canvas rendering
- chroma/effect rendering
- productive preview binding
- encoder, NDI, webcam, RTSP, MP4, audio

## Readiness Checklist

| Capability | Product contract | Implementation |
|---|---|---|
| Project, canvas, draw objects | Yes | Yes |
| Source/output typed settings | Yes | Foundation complete |
| Editor API | Yes | Foundation complete |
| Engine facade | Yes | Public runtime foundation complete |
| CP1 offscreen Vulkan path | Yes | Hardened for first-source visual proof |
| CP2/CP3 compositor | Yes | Blocked |
| Public SDK experience | Yes | Initial authoring/runtime/sink path exists |

Verdict: the product model foundation and initial public runtime/sink path are
in good shape. The next renderer work should be CP2 multi-layer, not media
integrations.
