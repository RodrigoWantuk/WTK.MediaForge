# CP3 Solid Layer Acceptance Report

Status: **accepted**

Validation date: 2026-06-22

## Supported Scope

Vulkan rendering of `SolidDrawObject` layers:

- Solid color fill
- Simple transform (position and size)
- Clipping against canvas bounds
- Opacity and normal alpha blending over source layers
- Single draw per solid layer (no duplicate geometry pass)

## Out of Scope

- Rotation (`render.transform_rotation_unsupported`)
- Crop (`render.crop_unsupported`)
- Non-normal blend modes (`render.blend_mode_unsupported`)

## Pixel Correctness Tests

File: `WTK.MediaForge.Graphics.Vulkan.Tests/Cp3SolidLayerTests.cs`

| Test | Validates |
|------|-----------|
| `Solid_layer_renders_expected_color` | Solid color output |
| `Solid_layer_blends_over_source_layer` | Alpha blend over source |
| `Solid_layer_respects_transform_and_clipping` | Transform + clip |
| `Solid_layer_opacity_0_5_blends_exactly_once_over_source_layer` | No double draw |
| `Solid_draw_object_does_not_report_render_drawobject_not_supported` | Supported draw object |
| `Solid_layer_rotation_reports_unsupported_diagnostic` | Rotation diagnostic |
| `Solid_layer_crop_reports_unsupported_diagnostic` | Crop diagnostic |

## Expected Diagnostics

| Code | When |
|------|------|
| `render.transform_rotation_unsupported` | Non-zero rotation on solid layer |
| `render.crop_unsupported` | Crop rect on solid layer |
| `render.drawobject_not_supported` | Must not appear for solid layers |

## Known Limitations

- Solid layers share the same unsupported transform subset as source layers.
- Acceptance is proven through offscreen pixel readback, not live preview.

## Acceptance Criteria

Solid layer rendering is accepted because pixel tests prove color, blend, transform,
clip, and single-pass opacity behavior, with explicit diagnostics for unsupported
transform features.
