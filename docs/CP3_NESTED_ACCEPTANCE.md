# CP3 Nested Canvas Acceptance Report

Status: **accepted**

Validation date: 2026-06-22

## Supported Scope

Vulkan rendering of `CanvasDrawObject` nested canvases:

- Child canvas rendered into parent canvas
- Simple transform on nested canvas layer
- Opacity on nested canvas layer
- Depth limit of 8 nested levels
- Intermediate GPU target retained until submission fence completes

## Out of Scope

- Rotation on nested canvas layers
- Crop on nested canvas layers
- Intermediate target pooling/cache (performance work before production preview)

## Pixel Correctness Tests

File: `WTK.MediaForge.Graphics.Vulkan.Tests/Cp3NestedCanvasTests.cs`

| Test | Validates |
|------|-----------|
| `Nested_canvas_renders_into_parent` | Basic nested composition |
| `Nested_canvas_respects_transform` | Transform on nested layer |
| `Nested_canvas_depth_8_works` | Depth-8 nesting |
| `Nested_canvas_target_lifetime_survives_submission` | Target survives until fence |
| `Canvas_draw_object_does_not_report_render_drawobject_not_supported` | Supported draw object |
| `Canvas_layer_rotation_reports_unsupported_diagnostic` | Rotation diagnostic |
| `Canvas_layer_crop_reports_unsupported_diagnostic` | Crop diagnostic |

## Lifetime Expectation

Intermediate targets created for nested canvases are owned by
`VulkanSubmissionResourceScope` and released only from
`VulkanRenderFrameSubmission.DisposeCompleted()` after fence completion.

## Known Limitations

**Performance:** intermediate targets are currently allocated per submit. Before
productive preview or production output, this must move to a keyed cache/pool:

```text
canvas id + size + format + generation
```

This limitation is accepted for CP3 initial delivery but is **not** accepted for
final product performance.

## Acceptance Criteria

Nested canvas rendering is accepted because pixel tests prove composition,
transform, depth-8 behavior, and submission-scoped intermediate target lifetime,
with explicit diagnostics for unsupported transform features.
