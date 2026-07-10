# WTK MediaForge Shader Catalog

Stable catalog ids for the modular composition shader family. Each draw object type maps to one pipeline kind through `RenderDrawObjectPipelineMapper`.

## Naming

| Catalog id | Pipeline kind | Draw object |
|---|---|---|
| `mf.source.layer` | SourceLayer | `SourceLayerDrawObject` |
| `mf.solid` | Solid | `SolidDrawObject` |
| `mf.text` | Text | `TextDrawObject` |
| `mf.canvas.composite` | CanvasComposite | `CanvasDrawObject` |
| `mf.output.letterbox` | OutputLetterbox | Render output target |

## GLSL Skeletons

Embedded in `WTK.MediaForge.Graphics.Vulkan/Shaders/Catalog/`:

| File | Role |
|---|---|
| `mf_common.vert` | Fullscreen triangle shared by layer pipelines |
| `mf_source_layer.frag` | Sampled source with crop-before-layout |
| `mf_solid.frag` | Solid fill |
| `mf_text.frag` | Pre-rasterized text texture |
| `mf_canvas_composite.frag` | Nested canvas texture |
| `mf_output_letterbox.frag` | Canvas fit into output surface |

## UV Pipeline

Layer shaders draw into the axis-aligned bounding box of the transformed object,
then map each fragment back into local layer UV using the inverse object
rotation around `Transform2D.Pivot`.

For sampled source layers, the order matches
`CoordinateSystem.CompositionPipelineOrder`:

```glsl
localUv = inverseObjectTransform(fragmentUv, geometryRect, boxSize, pivot, rotationDegrees);
croppedLogicalSize = logicalSize * crop.sizeFraction;
uvInCroppedContent = computeLayoutUv(localUv, layoutMode, croppedLogicalSize, boxSize);
uvLogical = mapCroppedUvToFullLogicalUv(uvInCroppedContent, crop);
uvRaw = rotateUvForTexture(uvLogical, contentRotation);
color = texture(sourceTexture, uvRaw);
```

For texture-like draw objects (`TextDrawObject`, `CanvasDrawObject`), crop maps
local UV into a sampled sub-rectangle. For `SolidDrawObject`, crop behaves as a
local mask because there is no source texture to sample.

## Managed API

- `ShaderPipelineKind`
- `ShaderPipelineDescriptor`
- `ShaderPipelineCatalog`
- `RenderDrawObjectPipelineMapper`

## Snapshot Ownership

`IRenderBackend.Submit` returns `IRenderFrameSubmission`, which owns the snapshot until completion cleanup. `MediaForgeRenderThread` uses `PendingRenderSubmissionTracker` to poll completed submissions and call `DisposeCompleted()` outside the tracker lock. The backend never disposes snapshots directly.

With `NullRenderBackend` / `ImmediateRenderFrameSubmission`, submissions complete immediately and are cleaned up on the next poll or during shutdown through `WaitForCompletionAsync(timeout, ct)` then `DisposeCompleted()`.

`LatestSnapshotBuffer.Publish` uses a lock; rejected publishes do not dispose the caller's snapshot. `PublishFrame` disposes on failure before ownership transfer.

## Current Status

The old `desktop_preview.vert/frag` POC path has been removed. Catalog shaders are the only shader family for product composition work.
