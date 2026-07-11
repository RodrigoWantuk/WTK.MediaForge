# Preview Panel Acceptance

`PreviewPanelSink` is an experimental GPU preview sink. It is allowed as a
local reliability track, but it is not yet a product preview feature.

## Product Boundary

- Input is a completed rendered output surface lease.
- Presentation is GPU-only; no CPU readback is allowed on the product preview path.
- The sink must not trigger a render. It consumes an existing `RenderOutput`.
- The sink must not expose Vulkan images, D3D11 textures, raw shared handles,
  render-thread types, command buffers, or fences through the public API.
- Slow presentation, stop, dispose, cancellation, and presenter failures must
  be observable and must not leak surface leases.

## Required Automated Evidence

- Rejects zero panel handles and unsupported backends.
- Rejects non-presentable surfaces and releases their leases.
- Releases leases when presentation succeeds, fails, or the callback fails.
- Stop waits for in-flight presentation; cancellation preserves the presenter.
- Dispose timeout is observable and allows retry instead of pretending success.
- Attach/detach, resize, start/stop, and slow-present cycles are stress-tested.

## Remaining Product Gate

Before `PreviewPanelSink` can become product-supported, run and document a local
Win32/Avalonia panel smoke test proving real window presentation, resize,
attach/detach, stop/dispose retry, and sustained delivery without CPU readback.
