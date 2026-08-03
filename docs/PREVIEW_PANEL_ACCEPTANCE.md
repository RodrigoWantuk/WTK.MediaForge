# Preview Panel Acceptance

`PreviewPanelSink` is an experimental GPU preview sink. The Windows/Avalonia
hosted-surface integration is the product-direction host boundary, while this
sink remains a separate presenter reliability track; preview is not yet a
product-supported feature.

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
- GPU smoke coverage presents a renderer-produced Vulkan output frame through
  `PreviewPanelSink` to a real Win32 panel handle and verifies lease, presenter,
  and pending command-buffer cleanup without CPU readback.

## Remaining Product Gate

Before `PreviewPanelSink` can become product-supported, run and document a
product-hosted Avalonia panel reliability pass proving real visible presentation,
interactive resize, attach/detach, stop/dispose retry, and sustained delivery
without CPU readback.

The current host owns the native surface and the engine owns the attachment.
Engine stop, project replacement, and disposal detach the surface; native-host
close detaches it before closing the surface. This ownership is implemented but
still requires the physical and sustained evidence above.
