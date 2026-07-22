# WTK MediaForge Product Model

This document is the product contract for WTK MediaForge: what users author,
save, validate, route, and operate. Runtime and GPU execution details live in
[ARCHITECTURE.md](../ARCHITECTURE.md). Public API boundaries live in
[PUBLIC_API.md](PUBLIC_API.md). Active implementation order lives in
[ROADMAP_CURRENT.md](ROADMAP_CURRENT.md).

If a new media feature does not fit this model, update this document before
adding code.

## Core Model

```text
MediaForgeProject
  -> SourceDefinitions
  -> Canvases / Scenes
  -> DrawObjects
  -> Effects
  -> RenderOutputs
  -> RenderOutputSink(s)
```

`MediaForgeCanvas` is the canonical scene object. Public APIs may use `Scene`
as ergonomic naming, but scene must remain an alias over canvas unless the
product model is explicitly changed.

## Product/Runtime Boundary

| Concern | Product layer | Runtime/GPU layer |
|---|---|---|
| Editable state | `MediaForgeProject`, editor/builder APIs | immutable snapshots |
| Source definitions | `MediaForgeSourceDefinition` + typed settings | providers, frame buffers, GPU leases |
| Canvas graph | `MediaForgeCanvas`, draw objects, effects | canvas/render snapshots |
| Outputs | `MediaForgeRenderOutput` + typed settings | output surfaces and sink dispatcher |
| Rendering | layout, opacity, crop, effect intent | Vulkan pipelines and command resources |
| Diagnostics | validation/runtime diagnostics | backend and lifecycle diagnostics |

Forbidden product-layer shortcuts:

- Source-specific draw objects such as `WebcamDrawObject`, `NdiDrawObject`, or `RtspDrawObject`.
- NDI, encoder, stream, or file logic inside `MediaForgeVulkanRenderer`.
- Chroma key as a source-layer property instead of an effect.
- Public APIs requiring render threads, backend factories, Vulkan registry types, snapshots, or GPU leases.

## Project

`MediaForgeProject` is the serializable root:

- `SourceDefinitions`
- `Canvases`
- `Outputs`
- schema/version metadata

Public callers should prefer `MediaForgeProjectBuilder`,
`MediaForgeProjectEditor`, typed helper factories, and package import/export
APIs for normal authoring.

## Sources

Sources are defined once and referenced by source layers. A source produces
frames; it does not render and does not know about canvases, layers, outputs, or
sinks.

Current public source setting contracts:

- `DesktopCaptureSourceSettings`
- `WindowCaptureSourceSettings`
- `WebcamSourceSettings`
- `ImageFileSourceSettings`
- `AnimatedImageSourceSettings`
- `LottieSourceSettings`
- `VideoFileSourceSettings`
- `RtspInputSourceSettings`
- `IpCameraSourceSettings`
- `NdiInputSourceSettings`
- `GeneratedSourceSettings`

Runtime adapters can land incrementally, but the product rule stays stable: one
source can feed many layers/scenes and every source frame has explicit
lease/lifetime ownership.

## Draw Objects

Current draw object model:

- `SourceLayerDrawObject`
- `TextDrawObject`
- `SolidDrawObject`
- `CanvasDrawObject`

Common properties include transform, crop, opacity, blend mode, enabled state,
and ordered effects. Draw objects describe intent; they never own GPU resources.

## Canvases / Scenes

Canvases hold ordered draw objects and may reference other canvases through
`CanvasDrawObject`.

Rules:

- cycles are invalid
- maximum nested canvas depth is 8
- disabled nested canvas objects do not acquire internal frames
- a canvas can be routed to multiple outputs
- reusable canvases should become render-graph dedupe points when size/config/version match

### Scene Editing Modes

Scene editing semantics are owned by the engine, not by Studio or another host
application.

`Live` editing mutates the published scene version transactionally. Once the
mutation validates, normal sinks and output routes observe the change on the
next rendered frame without restarting the route.

`Apply` editing creates a draft version for an edit session. Draft mutations are
visible only to draft/preview bindings for that session. Published sinks keep
rendering the currently published version until `ApplySceneDraftAsync` commits
the draft. `DiscardSceneDraftAsync` removes the draft without changing the
published project.

The public contracts are:

- `SceneEditMode`
- `SceneEditSessionId`
- `SceneVersionId`
- `SceneVersionBinding`
- `SceneMutationPatch`
- `SceneCommitRequest`
- `SceneCommitResult`

### Scene Versions And Nested Canvases

Every canvas has a published `SceneVersionId` in runtime state. A
`CanvasDrawObject` also carries a `SceneVersionBinding`:

- `Published` for normal outputs and sinks.
- `Draft` for edit-session previews.
- `ExplicitVersion` for transition boundaries that need to render old and new
  version graphs.

Canvas-as-source is a product feature. A canvas may be used as a layer inside
another canvas, but cycles are invalid and nesting depth remains bounded by
`CanvasGraphLimits.MaxNestedCanvasDepth`.

### Apply Propagation

When a draft is committed, the engine computes:

- direct canvas consumers;
- transitive canvas consumers;
- affected output routes;
- whether the requested transition policy should be applied by the output route.

For example:

```text
Canvas A v10 -> Canvas A v11
Canvas B contains Canvas A
Output Program renders Canvas B
```

Applying the draft for Canvas A publishes v11, invalidates Canvas B's version
graph, and reports the Program output as affected. Visual transition execution
belongs at the output route boundary; it must not become a permanent layer
effect.

## Effects

Effects are ordered product-model objects on draw objects:

- `ChromaKeyEffect`
- `ColorCorrectionEffect`
- `BlurEffect`

Transitions are not effects. They are owned by scene/output routing. The loader
migrates the obsolete schema-v1 `effect.transition` layer entry away and rejects
the discriminator in current project documents.

`ChromaKeyEffect` is the first renderer-supported source-layer effect. Other
effect contracts may exist before renderer support, but public docs and samples
must not imply rendering support until pixel/diagnostic tests prove it.

## Outputs And Sinks

Output definitions route canvases to completed rendered output frames.

Current public output setting contracts:

- `OffscreenOutputSettings`
- `PreviewWindowOutputSettings`
- `RecordingMp4OutputSettings`
- `EncodedFileOutputSettings`
- `StreamingRtmpOutputSettings`
- `StreamingSrtOutputSettings`
- `StreamingRtspOutputSettings`
- `StreamingHlsOutputSettings`
- `VirtualCameraOutputSettings`
- `NdiOutputSettings`

The output architecture is:

```text
Canvas -> RenderOutput -> internal GPU RenderOutputSurface -> RenderOutputSink(s)
```

The same `RenderOutput` can feed multiple sinks. Sinks do not trigger rendering
directly. Slow sinks must use explicit backpressure and must not block the render
thread.

`CpuReadbackSink` is a debug/sample/validation sink. `PreviewPanelSink` is the
validated Win32/Vulkan GPU preview sink for completed rendered surfaces without
CPU readback. Runtime-connected Studio preview, additional encoded sinks, NDI,
virtual camera, and audio outputs follow the roadmap order.

## Routing And Render Graph

The target routing model is:

```text
Source -> SourceLayer(s) -> Canvas/Scene -> RenderOutput -> RenderOutputSink(s)
```

The render graph target is:

```text
Outputs/Sinks -> RenderOutput -> Canvas/Scene -> DrawObjects -> Sources -> Effects
```

Deduplication rules:

- acquire the same source frame once per frame
- render identical reusable source/effect chains once when independent of placement
- render the same canvas once for the same size/config/version
- split only output-fit/presentation passes for different output sizes/layouts
- fan out one completed output frame to multiple sinks

The current render-graph compiler is a planning foundation and test target, not
yet the Vulkan execution scheduler.

## Package Serialization

Serializable package types:

- `MediaForgeProject` for full save/load
- `MediaForgeScenePackage` for one scene/canvas plus nested canvases, sources, effects, routed outputs, and metadata
- `MediaForgeCanvasPreset` for reusable layouts/PiP/mosaics/canvas arrangements
- `MediaForgeSourcePreset` for source definitions without runtime state
- `MediaForgeOutputPreset` for output profiles with secret-safe export by default
- `MediaForgeEffectPreset` for reusable effect chains

JSON may contain schema versions, stable ids, type ids, typed settings,
transforms, effects, canvas graph, output routes, and metadata.

JSON must not contain runtime leases, native handles, Vulkan/D3D11 objects,
command buffers, fences, backend worker state, sink queue state, or secrets
unless explicitly requested through export options.

Import modes:

- replace project
- merge as new scene
- merge presets only
- dry-run validation

Import builds and validates a candidate project before returning it. Failed
import and dry-run modes must not mutate the existing project.

## Engine

`MediaForgeEngine` is the public runtime facade. Current contracts:

- `CurrentProject` returns a deep clone.
- `LoadProjectAsync` and `ApplyProjectUpdateAsync` validate before swapping state.
- `StartAsync`, `StopAsync`, and `DisposeAsync` use explicit timeouts and deterministic cleanup.
- `AttachSinkAsync` and `DetachSinkAsync` connect public sinks to offscreen render outputs.
- Runtime failures use public typed exceptions and diagnostics.
- A failed or empty source must not crash the renderer or render pump.
