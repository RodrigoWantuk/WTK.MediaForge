# CP2 Acceptance Report

Status: **accepted**

Validation date: 2026-06-22

Current status note: vNext Commit 09 supersedes the original CP2 transform
limits. Source, solid, text, and nested-canvas draw objects now share the
renderer transform path for crop, rotation, and pivot handling.

Validation commands (all green):

```powershell
dotnet restore WTK.MediaForge.sln
dotnet build WTK.MediaForge.sln
dotnet test WTK.MediaForge.sln
./scripts/test.ps1 -Tier Fast
./scripts/test.ps1 -Tier Gpu
```

## Original Supported Scope

CP2 covers multi-layer source composition on one canvas and one offscreen
`RenderOutput` in Vulkan:

- Multiple `SourceLayerDrawObject` instances on the same canvas
- Same source bound to multiple layers
- Multiple distinct sources in one canvas
- Visual order follows canvas object list order
- Per-layer opacity and normal alpha blending
- Disabled layers are skipped
- Opacity zero layers are fully transparent
- Simple transform (position and size, no rotation)
- Clipping against canvas bounds

## Original CP2 Out of Scope

The following are explicitly not part of CP2:

- Rotation and pivot transforms (`render.transform_rotation_unsupported`, `render.transform_pivot_unsupported`)
- Crop (`render.crop_unsupported`)
- Text draw objects
- Productive preview UI
- Encoder, NDI, streaming
- Webcam, RTSP, MP4 decode sources
- Audio

## Pixel Correctness Tests

File: `Tests/WTK.MediaForge.Graphics.Vulkan.Tests/Cp2MultiLayerCompositionTests.cs`

| Test | Validates |
|------|-----------|
| `Cp2_same_source_two_layers_render_at_different_positions` | Same source, two positions |
| `Cp2_two_sources_render_expected_pixels` | Multiple sources |
| `Cp2_top_layer_overwrites_bottom_when_alpha_1` | Opaque top layer |
| `Cp2_top_layer_alpha_blends_over_bottom` | Alpha blend |
| `Cp2_layer_order_matches_canvas_object_order` | Z-order from list |
| `Cp2_layer_transform_positions_pixels_correctly` | Simple transform |
| `Cp2_disabled_layer_is_not_rendered` | Disabled layer |
| `Cp2_opacity_zero_layer_is_transparent` | Zero opacity |

## Lifetime and Stress Tests

File: `Tests/WTK.MediaForge.Graphics.Vulkan.Tests/Cp2MultiLayerStressTests.cs`

| Test | Validates |
|------|-----------|
| `Cp2_repeated_multi_layer_submits_do_not_exhaust_descriptor_pool` | Descriptor pool stability |
| `Cp2_multi_layer_submission_dispose_releases_framebuffers_descriptors_and_surfaces` | Submission cleanup after fence |

## Original Expected Diagnostics

At CP2 acceptance time, transform/crop requests outside the supported subset
emitted explicit render diagnostics instead of silent fallback. vNext Commit 09
removes those unsupported diagnostics for the shared transform path.

## Known Limitations

- Intermediate offscreen targets for nested canvases are CP3 scope, not CP2.
- `CpuReadbackSink` is used for pixel validation only; it is not a product preview path.
- CP2 acceptance is renderer-level. Engine/source/sink integration is covered by
  composition tests separately.

## Acceptance Criteria

CP2 is accepted because:

1. Build and Fast/Gpu tiers are green.
2. Pixel tests prove multi-layer ordering, blending, transform, and visibility rules.
3. Stress tests prove descriptor/framebuffer lifetime under repeated multi-layer submits.
