# Current Product Roadmap

This is the active roadmap for WTK MediaForge. Historical CP, phase, and
readiness documents under `docs/history` are evidence only and are not product
requirements.

## Product Contract

- Continuous video is hardware-first: GPU decode, GPU surfaces, Vulkan
  composition, GPU conversion, hardware encode, then encoded packets.
- Product paths never fall back to software decode/encode or move continuous
  uncompressed frames through CPU/RAM.
- Sources produce leased frames and know nothing about scenes or sinks.
- A scene is a `MediaForgeCanvas`; layers place sources, primitives, or nested
  canvases. `Live` and `Apply` semantics belong to the engine.
- Sinks consume completed output leases or validated encoded packets and never
  trigger rendering.
- Native resources stay in platform assemblies. Core uses stable logical ids,
  immutable snapshots, explicit capabilities, and asynchronous ownership.
- `Unavailable` is valid only with a concrete hardware, driver, API, license,
  or failed-proof reason. It is never a placeholder for omitted implementation.

## Current Reality

Implemented foundations:

- Transactional engine lifecycle, source runtime ownership, bounded sink
  workers, asynchronous GPU submission cleanup, and explicit timeout diagnostics.
- Vulkan multi-layer composition, nested canvases, transforms, crop, opacity,
  blend, solid/text, chroma key, color correction, blur, and cut/fade routing.
- Windows desktop duplication, Windows Graphics Capture for HWND sources,
  static images, Media Foundation webcam capture, hardware MP4 decode,
  Vulkan/D3D11 interop, Media Foundation H.264 encode, packet-only MP4 muxing,
  and TCP RTMP publishing.
- Published/draft scene versions, nested version binding, Apply propagation,
  old/new transition snapshots, and physical output/canvas/effect operations.
- Logical `CanvasId` values remain stable across Published, Draft, and Explicit
  bindings. Deterministic resolved canvas keys identify physical content and
  nested-version graphs, allowing equivalent outputs to share work without
  aliasing different pixels. Scene history retains the latest 32 versions per
  canvas plus pinned published, draft, and explicit bindings; retention
  counters are exposed in runtime health.
- Compatible MP4/RTMP routes share one rendered output, NV12 conversion, and
  hardware encoder; sinks retain independent bounded queues and status.
- H.264 profile and level are validated public enum contracts with legacy JSON
  migration. The Media Foundation session has transactional lifecycle states,
  rejects negotiated profile/level divergence, drains delayed packets before
  flush, and publishes requested/negotiated values in hardware proof evidence.
- Encoded grouping separates rendered-pixel, encoder, and sink compatibility:
  a destination or backpressure policy cannot alter pixel/encoder identity,
  while any profile difference prevents unsafe encoder sharing.
- Capability snapshots are cached by adapter/device generation. Vulkan and
  D3D11 adapters are matched by Windows LUID; cross-GPU interop fails closed.
- Automatic recovery policies expose public health snapshots, observe providers
  that fail asynchronously, recreate the Vulkan backend after submit/device
  failure, and isolate source, export, encoder, MP4, and RTMP failures.
- `proof.media_io.window_capture.product` creates a real HWND and validates
  WGC -> D3D11 GPU slot -> Vulkan on the active adapter. The sustained runtime
  tool validates a shared encode route through real MP4 and local RTMP sinks.

Experimental and not yet product-promoted:

- `PreviewPanelSink`: fence-timeout cleanup is retryable and no longer destroys
  in-flight resources, but hosted Avalonia resize/attach/detach and sustained
  presentation must pass before promotion.
- MP4 recording, RTMP, MP4 input, webcam, desktop, and window capture: real Windows
  implementations exist, but product availability remains hardware-dependent
  and requires the current composite proof report plus sustained route evidence.
- Physical RenderGraph: Vulkan consumes physical output/canvas and blur
  intermediate operations and validates topology/identity before command
  recording. Source acquisition, every effect, encoded dispatch, and all
  temporary-resource ownership are not yet exclusively graph-driven.
- Fault recovery: source restart, RTMP reconnect, and Vulkan backend recreation
  are wired. MP4 route recovery intentionally requires a new recording segment;
  silently overwriting an active recording is prohibited. Sustained fault
  injection for every matrix item remains a release activity.
- Studio: production bootstrap now uses a real engine session, canonical
  `MediaForgeProject` persistence, and asynchronous capability probing. Native
  GPU preview and real output controls remain disabled until their runtime gates pass.

Unavailable/planned:

- SRT, virtual camera, audio capture/mix/mux, and product NDI video.
- Linux VAAPI/DRM/DMABUF and macOS VideoToolbox/IOSurface adapters.
- NDI discovery/runtime packaging exists, but Standard SDK raw CPU video buffers
  do not satisfy the GPU Media Transport Law.

## v14 Execution Order

1. Close Desktop Duplication and preview presenter lifetime under repeated stop,
   timeout, resize, and device/display-reset cycles.
2. Sustain `Vulkan -> D3D11/NV12 -> MF H.264 -> MP4 + RTMP` with one compatible
   encode group and bounded resource counters.
3. Sustain `MP4 -> D3D11VA GPU lease -> Vulkan` including seek, pause, loop, EOF,
   reconnect, and shutdown during a frame in flight.
4. Connect recovery to physical device-lost recreation and prove isolation:
   RTMP failure must not interrupt recording; source loss must not stop unrelated routes.
5. Make the compiled physical RenderGraph the sole renderer input and move source,
   all effect intermediates, output fanout, and encoded dispatch behind it.
6. Add bounded GPU pools for render/effect/export intermediates with post-fence
   retirement diagnostics and baseline-return assertions.
7. Promote preview/desktop/window/webcam/video/MP4/RTMP only from sustained
   v14 evidence on the target adapter.
8. Integrate Studio preview and output controls after promotion; keep Avalonia
   overlays independent from the native presentation surface.
9. Freeze Core adapter contracts, then implement Linux and macOS backends in
   their own projects.

Scene identity and the first bounded retention store are complete. Remaining
retention work in v14 is to connect transition and submission ownership to
explicit pin handles and qualify baseline return under sustained Apply traffic.

## Readiness v14

The only current engine readiness entrypoint is:

```powershell
./scripts/verify-engine-readiness-v14.ps1
```

It runs one flat sequence: build, Fast, GPU, transport/license policies,
composite media proofs, real performance-tagged workloads, a short sustained
engine MP4+RTMP route, and writes
`test-reports/engine-readiness-v14.json` plus the media proof reports. The gate
performs a locked restore first and verifies that the aggregate status agrees
with the composite media report.

Release hardware validation uses:

```powershell
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Missing or skipped required hardware evidence returns exit code `2`. A normal
developer run may report hardware as unavailable, but cannot promote the feature.

Full local and release-candidate qualification are explicit modes:

```powershell
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia -RunLocalQualification
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia -ReleaseCandidateQualification
```

## Release Acceptance

- Local qualification: 30 minutes each for 1080p60 preview, preview+MP4+RTMP,
  MP4 decode-to-render, and nested Live/Apply transition.
- Release candidate: eight hours per target adapter family.
- Recording drops zero frames; streaming reports every drop/reconnect.
- RAM, VRAM estimate, handles, imports, slots, descriptor sets, and leases stay
  bounded after warm-up and return to baseline after stop.
- Fault matrix includes disk full, network disconnect, encoder failure, monitor
  disconnect, webcam removal, export failure, device lost, cancellation, and
  shutdown with work in flight.
- AMD RX 580 is the first mandatory Windows baseline. NVIDIA and Intel paths
  must be capability-detected and may not rely on vendor-specific assumptions.

## Deferred Scope

Audio, SRT, virtual camera, FFmpeg, and product NDI video remain outside v14.
FFmpeg/libav may only be reconsidered after native sustained routes and a
separate encoded-packet/container legal review.
