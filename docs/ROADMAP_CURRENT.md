# Current Product Roadmap

This is the active execution roadmap for WTK MediaForge.

Historical CP, phase, readiness, and action-plan documents under `docs/history` are evidence only. They do not override this file, `AI_CONTEXT.md`, `PRODUCT_MODEL.md`, `PUBLIC_API.md`, `ARCHITECTURE.md`, or the current support matrices.

See `docs/README.md` for the complete documentation authority order.

## Product contract

WTK MediaForge is a GPU-first media composition engine and native Avalonia Studio with a cross-platform product architecture.

The following rules are not negotiable:

- continuous video decode and encode use hardware acceleration or the feature is unavailable;
- continuous uncompressed video remains in GPU memory on product paths;
- sources produce leased frames and do not know about scenes or sinks;
- `MediaForgeCanvas` is the canonical scene object;
- layers place reusable sources, primitives, or nested canvases;
- `Live` and `Apply` editing semantics belong to the engine;
- sinks consume completed output leases or validated encoded packets and never trigger rendering;
- native resources remain in platform assemblies;
- Core uses stable logical ids, immutable snapshots, explicit capability truth, and asynchronous ownership;
- unsupported hardware, missing proof, incomplete adapters, and license blockers are reported explicitly rather than hidden by fallback code;
- hardware media paths never fall back to software decode/encode.

## Current product reality

### Implemented foundations

#### Product model and API

- Canonical `MediaForgeProject` serialization, validation, migration, builders, editors, typed source/output settings, presets, and package contracts.
- Public engine lifecycle, capability, scene-editing, sink, output-route, health, and diagnostics contracts.
- Stable logical identity separated from native handles and physical resource identity.
- Canonical disabled-output persistence and secret-safe project boundaries.

#### Engine lifecycle and ownership

- Transactional load/update/start/stop behavior.
- Source runtime ownership and asynchronous provider cleanup.
- Bounded sink workers and explicit backpressure.
- Submission tracking with fence-aware cleanup and timeout diagnostics.
- Recovery coordination for sources, RTMP routes, exports, encoders, and Vulkan backend failure.
- Aggregate live, retired, cached, pending-fence, high-water, scene-version, pin, queue, and route health counters.

#### Scene model and editing

- Multi-layer canvas composition.
- Canvas-as-source with cycle rejection and bounded nesting depth.
- Published, draft-session, and explicit-version bindings.
- Live transactional publication.
- Apply draft isolation, commit, discard, transitive parent invalidation, and affected-output calculation.
- Old/new route snapshots for Cut/Fade transitions.
- Bounded scene history with direct and transitive pins.
- Visual fingerprints shared by versioning, dirty classification, and render-graph cache identity.

#### Physical RenderGraph

- Logical and physical planning for source acquisition, layer transforms, effect intermediates, canvases, nested canvases, output passes, transitions, fan-out, and encoded dispatch.
- Topology, dependency, identity, output coverage, source acquisition, and encoded-dispatch validation before native execution.
- Product Vulkan submission requires a physical plan; test-only synthesis remains isolated.
- Vulkan external texture imports are constrained by physical source-acquisition operations.
- Encoded frame delivery is constrained by physical encoded-output dispatch operations.
- Physical operation identity no longer depends on parsing operation-key text.

#### Windows physical media path

- Desktop Duplication capture.
- Windows Graphics Capture for HWND sources.
- Media Foundation webcam capture with immediate OS-boundary GPU upload.
- PNG/JPEG static image load-time decode and GPU upload.
- Media Foundation MP4 hardware decode accepting GPU-backed samples only.
- D3D11 shared textures and Vulkan external-memory interop with adapter matching by LUID.
- Vulkan composition and GPU output surfaces.
- D3D11/NV12 export and Media Foundation hardware H.264 encode.
- Packet-only MP4 writing.
- TCP RTMP/FLV publishing with bounded queues and reconnect behavior.
- Compatible MP4/RTMP routes sharing rendered pixels, conversion, and hardware encoder while retaining independent sinks.

#### Studio

- Native Avalonia/MVVM shell with Design/Test and Runtime composition boundaries.
- Canonical project open/save with clone validation and atomic replacement.
- Preservation of canonical fields not represented by current editors.
- Scenes, reusable sources, layers, contextual properties, outputs, production cards, and dock layout persistence.
- Canvas selection, zoom, pan, move, resize, nudge, grid, safe areas, lock, visibility, reorder, and undo/redo.
- Explicit Draft and Live modes connected to engine scene-editing services.
- Apply/Discard using engine sessions and engine-reported affected outputs.
- Real engine lifecycle state and deterministic project/application shutdown orchestration.
- Real proof-gated MP4/RTMP activation, route metrics, reconnect state, elapsed recording time, and numbered segment rollover.
- Headless shell tests, automation ids, accessibility names, and visual QA at supported resolutions.

#### Portable audio foundation

- Serializable global audio graph with sources, nodes, connections, buses, routes, and sinks.
- Immutable compiled plans and transactional publication.
- Pooled planar float32 blocks at 48 kHz.
- Generated tone and silence sources.
- Gain, mute, pan, polarity, mixing, meters, and one-quantum fixed delay.
- Deterministic source/node DAG execution into buses.
- Bounded Program Mix route fan-out.
- Queue/pool pressure isolated to the affected route without blocking or faulting the callback path.
- Clock, timestamp, latency, drift, resampling, and A/V mapping contracts.

#### Remote Scene coordination

- Platform-neutral publish/subscribe, packet-lease, state, telemetry, reorder, keyframe-feedback, and hardware-decode-pump contracts.
- Separately deployable HTTPS/WebSocket signaling service.
- Hashed one-time invitations and access tokens.
- Role-scoped sessions, bounded SDP/ICE relay, SQLite state, quotas, rate limits, trusted-proxy policy, and coturn-compatible credentials.
- Versioned managed/native WebRTC C ABI contract and reproducible native supply-chain pin.

#### Validation and CI

- Mandatory Windows and Linux self-hosted CI jobs.
- Locked restore, Release build, portable test classification audit, Fast gate, media-transport policy, and license policy.
- Dedicated manual RX 580 hardware-media qualification job.
- Current readiness, final gate, media proof, Studio visual QA, and sustained qualification scripts.

### Experimental and proof-gated

The following have real implementations but are not automatically product-promoted on every machine:

- hosted GPU preview through the Windows/Avalonia hosted-surface path; `PreviewPanelSink` remains a separate experimental presenter reliability track;
- desktop capture;
- window capture;
- webcam capture;
- MP4 file input;
- MP4 recording;
- RTMP publishing;
- full Vulkan/D3D11 device-lost recovery;
- all physical RenderGraph ownership under sustained in-flight pressure.

Promotion requires the matching capability proof and sustained qualification on the active adapter/driver.

### Implemented but not product-available

- Portable audio graph processing and in-memory Program Mix routes exist, but physical audio capture, playback, encode, and A/V mux do not.
- Remote Scene signaling exists, but media publish/subscribe does not.
- NDI runtime detection and discovery exist, but product NDI video does not.
- Linux and macOS platform boundaries exist, but physical media adapters do not.

### Planned or deferred

- Linux VAAPI/DRM/DMABUF and vendor-specific GPU media adapters.
- macOS VideoToolbox/CVPixelBuffer/IOSurface/Metal media adapters.
- Physical audio adapters and A/V mux.
- Functional pinned libwebrtc encoded-access-unit adapter and Direct/TURN media qualification.
- RTSP/IP-camera input.
- Animated GIF/APNG/WebP and Lottie product paths.
- Product NDI video.
- SRT.
- Virtual camera.
- Advanced masks, temporal effects, plugins, complex transitions, and later production features.
- FFmpeg/libav review for encoded-packet/container-only use. It remains outside the current native product path.

## Active functional milestone

The current delivery checkpoint is defined by
[`MVP_API_STUDIO.md`](MVP_API_STUDIO.md).

The milestone must produce:

- a public .NET API quickstart that authors and operates a real nested composition through public contracts;
- native hosted GPU preview;
- proof-gated MP4 and RTMP output routing through public APIs;
- physical Live and Apply operation while outputs are active;
- an Avalonia Studio completing the same workflow without fake product services;
- deterministic stop/dispose and resource baseline return;
- mandatory Windows/Linux baseline validation and reviewed Windows hardware evidence.

MVP is used here only as a delivery milestone and integration checkpoint. It is
not a support-status label and does not authorize reduced architecture,
temporary media paths, software media fallback, raw-video pipes, fake preview,
model-only support claims, or bypassed validation.

## Current execution order

### 1. Documentation and public contract alignment

- Keep normative documents consistent with current source and tests.
- Audit the public API needed by the functional workflow.
- Add and maintain the API quickstart sample.

Exit criteria:

- no normative document describes portable mixing as absent;
- no normative document describes the Physical RenderGraph as planning-only;
- the quickstart workflow requires no internal runtime type.

### 2. Physical RenderGraph authority

- Complete graph-owned source acquisition.
- Complete graph-owned effect and temporary-resource operations.
- Complete output fan-out and encoded dispatch authority.
- Reject any production submission whose physical plan is incomplete or divergent.

Exit criteria:

- production Vulkan execution consumes only validated physical operations;
- no production side path independently discovers sources, effects, outputs, or encoded routes;
- resource ownership and diagnostics correspond to physical operations.

### 3. Hosted native preview

- Finalize the platform-neutral hosted-surface lifecycle.
- Implement the Windows Avalonia native host.
- Qualify attach, resize/DPI, rebind, dock/undock, minimize/restore, timeout, detach, and close.

The portable lifecycle, engine-authoritative attachment registry, Windows render-target bridge,
and Studio.Windows `NativeControlHost` integration are implemented. Hosted preview remains
Experimental and proof-gated until the qualification evidence below is current.

Exit criteria:

- 30-minute 1080p60 preview passes;
- no continuous CPU readback;
- in-flight resources survive timeout correctly;
- counters return to baseline after stop.

### 4. Public API vertical

- Complete product-level preview, MP4, and RTMP activation APIs.
- Add the canonical API quickstart.
- Qualify nested canvas, Live, Apply, preview, MP4, RTMP, failure isolation, and shutdown in one workflow.

Exit criteria:

- compatible MP4+RTMP routes render/convert/encode once;
- recording has no silent drops;
- RTMP reports every drop and reconnect;
- Live and Apply do not restart unrelated outputs;
- public hosts do not wire internal services.

### 5. Studio vertical

- Replace remaining placeholder source/output editing with typed canonical settings.
- Add explicit scene-as-source workflow.
- Bind the hosted preview below the Avalonia overlay.
- Complete real output cards, capability reasons, diagnostics, project replacement, and shutdown behavior.

Exit criteria:

- the production workflow completes without fake services;
- save/open preserves canonical fields;
- rejected Live edits preserve the last valid scene;
- Apply marks only engine-reported outputs;
- visual QA, accessibility, keyboard, and docking scenarios pass.

### 6. Functional milestone qualification

Run and review:

```powershell
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia
./scripts/verify-studio-ui-visual-qa.ps1
./scripts/verify-final-gate.ps1 -RequireHardwareMedia
```

Required sustained workloads:

- 30 minutes: hosted 1080p60 preview;
- 30 minutes: preview + MP4 + RTMP shared route;
- 30 minutes: MP4 hardware decode to Vulkan render;
- 30 minutes: nested Live/Apply transitions with active outputs.

Release-candidate qualification remains eight hours per target adapter family.

### 7. Physical audio

After the API/Studio video vertical is accepted:

- finalize channel mapping and adapter buffering;
- implement Windows physical capture/playback adapters;
- implement explicit device selection/removal behavior;
- integrate Program Bus controls into Studio;
- add hardware encode/mux only through an approved A/V architecture.

Portable mixing must remain allocation-free and bounded on the callback path.

### 8. Linux physical media

After portable contracts are stable and the Windows vertical is sustained:

- implement VAAPI/DRM PRIME/DMABUF and/or approved vendor adapters;
- implement Linux preview presentation;
- qualify capture, decode, encode, preview, MP4, and RTMP without CPU video staging.

### 9. Remote Scene media and later expansion

Only after shared encode/decode lifetimes are sustained:

- build the functional pinned libwebrtc adapter;
- connect encoded publish and receive-side hardware decode;
- run 30-minute Direct and TURN qualification between two machines;
- then resume advanced effects, masks, NDI video, SRT, virtual camera, and other deferred features according to capability and license review.

## Current validation entrypoints

Baseline:

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release
./scripts/test.ps1 -Tier Fast
```

GPU-sensitive changes:

```powershell
./scripts/test.ps1 -Tier Gpu
```

Current engine readiness is the only current engine readiness entrypoint:

```powershell
./scripts/verify-engine-readiness-v14.ps1
./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Aggregate release gate:

```powershell
./scripts/verify-final-gate.ps1 -RequireHardwareMedia
```

Omitting a required hardware or Remote Scene switch is developer validation only. It cannot promote the omitted capability.

## Release acceptance principles

- Recording never silently drops frames.
- Streaming reports every drop, reconnect, and terminal failure.
- Source, sink, network, encoder, export, and device failures remain isolated where architecture permits.
- RAM, VRAM estimates, handles, imports, slots, targets, framebuffers, descriptor sets, packets, queues, and leases remain bounded after warm-up and return to baseline after stop.
- AMD RX 580 is the first mandatory Windows hardware baseline.
- NVIDIA and Intel support remains runtime-detected and may not depend on vendor assumptions.
- Windows/Linux baseline CI is mandatory for every change and is not replaced by hardware qualification.
- Documentation must describe current implementation and proof status, not intended future capability.
