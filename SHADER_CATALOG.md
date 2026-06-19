# WTK MediaForge Shader Catalog

Stable catalog ids for the modular composition shader family. Each draw object type maps to one pipeline kind via `RenderDrawObjectPipelineMapper`.

## Naming

| Catalog id | Pipeline kind | Draw object |
|---|---|---|
| `mf.source.layer` | SourceLayer | `SourceLayerDrawObject` |
| `mf.solid` | Solid | `SolidDrawObject` |
| `mf.text` | Text | `TextDrawObject` |
| `mf.canvas.composite` | CanvasComposite | `CanvasDrawObject` |
| `mf.output.letterbox` | OutputLetterbox | Render output target (canvas → surface) |

## GLSL skeletons

Embedded in `WTK.MediaForge.Graphics.Vulkan/Shaders/Catalog/`:

| File | Role |
|---|---|
| `mf_common.vert` | Fullscreen triangle shared by layer pipelines |
| `mf_source_layer.frag` | Sampled source with crop-before-layout |
| `mf_solid.frag` | Solid fill |
| `mf_text.frag` | Pre-rasterized text texture |
| `mf_canvas_composite.frag` | Nested canvas texture |
| `mf_output_letterbox.frag` | Canvas fit into output surface |

## UV pipeline (source layer)

Order is fixed and matches `CoordinateSystem.CompositionPipelineOrder`:

```glsl
croppedLogicalSize = logicalSize * crop.sizeFraction;
uvInCroppedContent = computeLayoutUv(localUv, layoutMode, croppedLogicalSize, boxSize);
uvLogical = mapCroppedUvToFullLogicalUv(uvInCroppedContent, crop);
uvRaw = rotateUvForTexture(uvLogical, contentRotation);
color = texture(sourceTexture, uvRaw);
```

## Managed API

- `ShaderPipelineKind` — enum
- `ShaderPipelineDescriptor` — catalog metadata
- `ShaderPipelineCatalog` — registry
- `RenderDrawObjectPipelineMapper` — draw object → pipeline kind

## POC note

`desktop_preview.vert/frag` remain the active POC preview path. Catalog shaders are the foundation for the compositor backend (phase 2 Vulkan bridge).
