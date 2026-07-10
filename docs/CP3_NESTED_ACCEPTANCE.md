# CP3 Nested Canvas Acceptance Report

Status: **accepted**

Validation date: 2026-06-22

Current status note: vNext Commit 09 supersedes the original CP3 nested-canvas
transform limits. Nested canvas layers now use the shared renderer transform
path for crop, rotation, and pivot.

## Original Supported Scope

Vulkan rendering of `CanvasDrawObject` nested canvases:

- Child canvas rendered into parent canvas
- Simple transform on nested canvas layer
- Opacity on nested canvas layer
- Depth limit of 8 nested levels
- Intermediate GPU target retained until submission fence completes

## Original CP3 Out of Scope

- Rotation on nested canvas layers
- Crop on nested canvas layers
- Output/product preview reliability beyond the nested-canvas renderer scope

## Pixel Correctness Tests

File: `WTK.MediaForge.Graphics.Vulkan.Tests/Cp3NestedCanvasTests.cs`

| Test | Validates |
|------|-----------|
| `Nested_canvas_renders_into_parent` | Basic nested composition |
| `Nested_canvas_respects_transform` | Transform on nested layer |
| `Nested_canvas_depth_8_works` | Depth-8 nesting |
| `Nested_canvas_target_lifetime_survives_submission` | Target survives until fence |
| `Canvas_draw_object_does_not_report_render_drawobject_not_supported` | Supported draw object |
| `Canvas_layer_rotation_is_supported_without_unsupported_diagnostic` | Rotation accepted by current renderer |
| `Canvas_layer_crop_is_supported_without_unsupported_diagnostic` | Crop accepted by current renderer |

## Lifetime Expectation

Intermediate targets created for nested canvases are owned by
`VulkanSubmissionResourceScope` and released only from
`VulkanRenderFrameSubmission.DisposeCompleted()` after fence completion.

## Post-Acceptance Note

The initial CP3 acceptance did not include intermediate target pooling. That
performance work has since landed through `VulkanIntermediateTargetPool`; this
report remains the renderer acceptance record for nested canvas correctness and
submission-scoped lifetime.

## Acceptance Criteria

Nested canvas rendering is accepted because pixel tests prove composition,
transform, depth-8 behavior, and submission-scoped intermediate target lifetime.
vNext Commit 09 extends that foundation with shared crop, rotation, and pivot
handling.
