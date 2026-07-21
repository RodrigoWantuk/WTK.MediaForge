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
- Canonical canvas/draw-object visual fingerprints now drive scene versioning,
  dirty classification, and RenderGraph cache keys. Layer ordering and all
  supported visual properties invalidate consistently while metadata-only
  renames preserve the compiled graph.
- Logical `CanvasId` values remain stable across Published, Draft, and Explicit
  bindings. Deterministic resolved canvas keys identify physical content and
  nested-version graphs, allowing equivalent outputs to share work without
  aliasing different pixels. Scene history retains the latest 32 versions per
  canvas plus pinned published, draft, and explicit bindings and recursively
  pins explicit dependencies found inside historical snapshots. Direct,
  transitive, discarded, high-water, and resolution-failure counters are
  exposed in runtime health.
- Compatible MP4/RTMP routes share one rendered output, NV12 conversion, and
  hardware encoder; sinks retain independent bounded queues and status.
- Shared encoded routes support dynamic logical consumer activation and
  bounded per-sink drain/finalization, preserving healthy peers.
- H.264 profile and level are validated public enum contracts with legacy JSON
  migration. The Media Foundation session has transactional lifecycle states,
  rejects negotiated profile/level divergence, drains delayed packets before
  flush, and publishes requested/negotiated values in hardware proof evidence.
- Encoded grouping separates rendered-pixel, encoder, and sink compatibility:
  a destination or backpressure policy cannot alter pixel/encoder identity,
  while any profile difference prevents unsafe encoder sharing.
- Runtime health exposes aggregate live, retired, pending-fence, and cached GPU
  resources together with texture, intermediate-target, framebuffer, and
  descriptor-set high-water marks. These counters make sustained qualification
  able to detect unbounded growth and verify return to baseline after stop.
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
- Physical RenderGraph: the product Vulkan backend accepts only snapshots with
  an executed physical plan and validates topology/identity before importing
  textures or recording commands. An explicit low-level-test factory alone may
  synthesize plans for renderer tests. Source acquisition, every effect,
  encoded dispatch, and all temporary-resource ownership are not yet physical
  operations owned exclusively by the graph.
- Fault recovery: source restart, RTMP reconnect, and Vulkan backend recreation
  are wired. MP4 route recovery intentionally requires a new recording segment;
  silently overwriting an active recording is prohibited. Sustained fault
  injection for every matrix item remains a release activity.
- Studio: production bootstrap uses a real engine session and asynchronous
  capability probing. Disabled outputs are now canonical `MediaForgeProject`
  state and survive Studio round trips without creating runtime routes.
  `StudioProjectSession` now applies UI edits to a cloned canonical project,
  preserves extension settings, encode profiles, color/output configuration,
  advanced text state, nested version bindings, and effects not editable by the
  current UI, and commits only after atomic file replacement succeeds. Native
  GPU preview remains disabled until its runtime gate passes. MP4/RTMP controls
  now activate real proof-gated routes, report health/metrics and elapsed time,
  and roll restarted recording to a new numbered segment.
  The shell now reflects engine Starting/Running/Degraded/Recovering/Failed/
  Stopped state and performs deterministic project-switch/application shutdown;
  all active scene drafts are explicitly discarded before project replacement.
  Scene Apply reflects exactly engine-reported affected output ids and the
  route-owned Cut/Fade transition policy.
  Draft and Live editing are now explicit Studio modes. Live activation is
  confirmed when outputs are active, publishes coalesced atomic mutations
  without Apply, reports rejection without replacing the last valid scene, and
  deterministically discards its runtime session when leaving the mode.
- Remote Scene has platform-neutral contracts, bounded encoded-packet leases,
  a GPU-only hardware decode pump contract, a physical qualification schema,
  and a separately deployable
  HTTPS/WebSocket signaling service with one-time hashed invitations, role-scoped
  bearer access, bounded SDP/ICE relay, SQLite session storage, expiration, rate
  limiting, and coturn-compatible temporary credentials. Signaling carries no
  media. The C ABI and managed bindings are pinned and contract-tested, but the
  checked-in native target deliberately reports its backend unavailable. A
  functional pinned libwebrtc adapter and Direct/TURN physical GPU evidence are
  not present, so publish/subscribe remain unavailable.

Unavailable/planned:

- SRT, virtual camera, audio capture/mix/mux, and product NDI video.
- Linux VAAPI/DRM/DMABUF and macOS VideoToolbox/IOSurface adapters.
- NDI discovery/runtime packaging exists, but Standard SDK raw CPU video buffers
  do not satisfy the GPU Media Transport Law.
- Remote Scene publish/subscribe media remains unavailable until a pinned native
  libwebrtc bridge and direct/TURN GPU end-to-end proofs pass. The signaling
  service alone is not media capability evidence.

## v14 Execution Order

1. Close Desktop Duplication and preview presenter lifetime under repeated stop,
   timeout, resize, and device/display-reset cycles.
2. Sustain `Vulkan -> D3D11/NV12 -> MF H.264 -> MP4 + RTMP` with one compatible
   encode group and bounded resource counters.
3. Sustain `MP4 -> D3D11VA GPU lease -> Vulkan` including seek, pause, loop, EOF,
   reconnect, and shutdown during a frame in flight.
4. Connect recovery to physical device-lost recreation and prove isolation:
   RTMP failure must not interrupt recording; source loss must not stop unrelated routes.
5. Extend the now-mandatory compiled physical RenderGraph so source acquisition,
   all effect intermediates, output fanout, and encoded dispatch execute as
   graph-owned physical operations.
6. Extend the bounded GPU pools and live/high-water diagnostics already used by
   Vulkan targets, framebuffers, descriptor sets, and textures to every export
   intermediate, then enforce baseline-return assertions in sustained runs.
7. Promote preview/desktop/window/webcam/video/MP4/RTMP only from sustained
   v14 evidence on the target adapter.
8. Integrate Studio preview and output controls after promotion; keep Avalonia
   overlays independent from the native presentation surface.
9. Freeze Core adapter contracts, then implement Linux and macOS backends in
   their own projects.
10. Build the functional pinned libwebrtc adapter behind the frozen C ABI only
    after shared encode/decode lifetimes are sustained; then qualify Remote
    Scene direct and TURN routes without software codecs or raw CPU frames. ABI
    contract-test success alone cannot promote the feature.

Scene identity and bounded retention are complete. The store retains the latest
32 versions per canvas in addition to older pinned versions. Drafts and output
transitions own explicit pin handles, replacement/completion releases them, and
runtime health exposes retained, pinned, discarded, and high-water counts.
Remaining v14 work is sustained Apply qualification with baseline-return
assertions across transitions and submissions.

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
- Remote Scene requires separate 30-minute Direct and TURN reports from two
  machines, all scenarios defined by `RemoteSceneQualificationGate`, no raw CPU
  video, deterministic shutdown/reconnect, and resource baseline return.

The aggregate documented entrypoint is `scripts/verify-final-gate.ps1`. It
keeps hardware and Remote Scene requirements explicit so a portable developer
run cannot accidentally promote unavailable media.

## Deferred Scope

Audio, SRT, virtual camera, FFmpeg, and product NDI video remain outside v14.
FFmpeg/libav may only be reconsidered after native sustained routes and a
separate encoded-packet/container legal review.
