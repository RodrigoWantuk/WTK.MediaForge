# CP3 ChromaKeyEffect Acceptance Report

Status: **accepted**

Validation date: 2026-06-22

## Supported Scope

`ChromaKeyEffect` on source layers in Vulkan:

- Key color removal on source layers
- Similarity, smoothness, and spill reduction parameters
- Disabled effects are not applied
- Effect order is preserved when multiple effect slots exist
- Only one active chroma key is supported at a time

## Out of Scope

- Any effect other than `ChromaKeyEffect` on source layers
- Add blend modes on source layers
- Text draw objects
- MP4/video timeline playback (deferred until timeline clock exists)

## Pixel Correctness Tests

File: `WTK.MediaForge.Graphics.Vulkan.Tests/Cp3ChromaKeyEffectTests.cs`

| Test | Validates |
|------|-----------|
| `Chroma_key_removes_key_color` | Basic keying |
| `Chroma_key_respects_similarity_smoothness` | Key parameters |
| `Disabled_effect_is_not_applied` | Disabled effect skipped |
| `Effect_order_is_preserved` | Effect ordering semantics |

## Diagnostic Tests

| Test | Validates |
|------|-----------|
| `Chroma_key_invalid_configuration_reports_diagnostic` | `render.effect_invalid` |
| `Multiple_chroma_key_effects_report_diagnostic` | Multiple active chroma keys rejected |
| `Source_layer_unsupported_effect_reports_render_effect_not_supported` | Unsupported effects |
| `Add_blend_mode_reports_render_blend_mode_unsupported` | Unsupported blend mode |
| `Text_draw_object_reports_render_drawobject_not_supported` | Unsupported draw object |

## Expected Diagnostics

| Code | When |
|------|------|
| `render.effect_invalid` | Invalid chroma configuration |
| `render.effect_not_supported` | Unsupported effect type or multiple active chroma keys |
| `render.blend_mode_unsupported` | Non-normal blend mode on source layer |
| `render.drawobject_not_supported` | Unsupported draw object types such as text |

## Product Decisions

- `ChromaKeyEffect` is the **only** real source-layer effect accepted at this milestone.
- `CpuReadbackSink` remains a debug/sample/validation sink, not the primary preview or encoder path.
- `TimelineDriven` source mode currently behaves as keep-latest placeholder until
  `MediaTimelineClock`, timestamp frame selection, seek, and end-of-stream policy exist.
  MP4/video file sources remain deferred until that work lands.

## Acceptance Criteria

ChromaKeyEffect is accepted because pixel tests prove keying behavior and parameter
semantics, and diagnostic tests prove explicit failure modes for invalid,
duplicate, and unsupported configurations.
