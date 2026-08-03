# Functional API and Studio Milestone

## Purpose

This document defines the next integrated delivery checkpoint for WTK MediaForge:

- a public API that can author, load, run, edit, preview, record, and stream a real composition on Windows through product contracts; and
- an Avalonia Studio that exposes the same engine path without mock success or a competing project model.

This checkpoint is called an MVP only as a delivery milestone. It does not redefine the final product, reduce the architecture, or permit temporary product paths that violate the project contracts.

## Non-negotiable boundaries

The milestone must preserve all of the following:

- continuous uncompressed video remains in GPU memory on product paths;
- continuous decode and encode use hardware acceleration or report `Unavailable`;
- no software codec fallback, raw-video pipe, or primary CPU-readback preview;
- `MediaForgeProject` is the only persisted product document;
- sources produce leased frames and do not know about scenes or sinks;
- sinks consume completed rendered surfaces or validated encoded packets and never trigger rendering;
- scene `Live` and `Apply` semantics are implemented by the engine;
- canvas-as-source, version binding, cycle detection, and transitive Apply propagation remain functional;
- capability status and unavailable reasons reflect real runtime evidence;
- Windows and Linux portable build/test gates remain mandatory;
- native APIs remain isolated in platform projects;
- no feature is marked supported from model presence, nominal GPU names, prototype code, or skipped tests.

## Deliberately outside this milestone

The following remain valid product areas but do not block this delivery checkpoint:

- physical audio capture, playback, encode, and A/V mux;
- Linux and macOS physical media adapters;
- Remote Scene media transport;
- product NDI video;
- SRT;
- virtual camera;
- RTSP/IP-camera input;
- animated-image and Lottie product paths;
- advanced masks, temporal effects, plugins, and complex transition systems.

Their contracts must remain valid and their capability status must remain truthful.

# API milestone

## Required user workflow

A .NET application must be able to perform this sequence using public APIs only:

1. Create or load a canonical `MediaForgeProject`.
2. Define reusable sources.
3. Create at least two canvases, including one canvas used as a layer in another.
4. Add source, text, and solid layers with transforms, crop, opacity, blend, and currently supported effects.
5. Define an enabled preview output and enabled MP4/RTMP outputs where capabilities permit.
6. Create the Windows engine through the public platform entrypoint.
7. Probe capabilities asynchronously and show concrete unavailable reasons.
8. Load and start the project.
9. Observe completed output without accessing Vulkan, D3D11, command buffers, fences, snapshots, or internal runtime services.
10. Perform one `Live` edit while an output is active.
11. Perform one `Apply` draft edit and commit it while an output is active.
12. Record a valid H.264 MP4 segment when the hardware proof gate permits it.
13. Publish to a local RTMP endpoint when the hardware/network proof gate permits it.
14. Stop and dispose deterministically with resources returning to baseline.

## Required API deliverables

- A maintained `samples/WTK.MediaForge.Sample.ApiQuickstart` sample using only public API.
- Public XML documentation for the primary authoring, engine lifecycle, capability, scene-editing, and output-routing entrypoints used by the sample.
- No sample dependency on internal projects or test-only factories.
- Stable typed settings for all sources and outputs used by the sample.
- A documented error path for validation failure, unavailable capability, route failure, and shutdown timeout.
- A qualification test that executes the same logical workflow without substituting fake product success.

## API acceptance criteria

### Portable contract acceptance

- Project build/edit/serialize/load round-trip passes on Windows and Linux.
- Nested canvases, cycle rejection, depth limits, Live mutations, Apply drafts, affected-output calculation, and bounded scene-version retention are covered by portable tests.
- Public API guard tests reject exposure of native handles and internal runtime types.

### Windows physical acceptance

- Static image or generated content renders through the production Vulkan path.
- At least one real capture source renders through a GPU lease on the active adapter.
- Hosted preview runs for 30 minutes with resize and detach/attach cycles.
- MP4 records for 30 minutes with zero silent frame drops.
- RTMP runs for 30 minutes with explicit drop/reconnect accounting.
- A shared compatible MP4+RTMP route renders, converts, and encodes once.
- Live and Apply edits operate during the active route without restarting unrelated outputs.
- Stop/dispose returns tracked RAM, handles, leases, imports, targets, framebuffers, descriptor sets, and route queues to baseline.

# Studio milestone

## Required user workflow

A user must be able to complete this sequence in the Avalonia Studio:

1. Create a new project or open an existing canonical project.
2. Add a supported source through a capability-aware source library.
3. Create and rename scenes.
4. Add sources to scenes as layers.
5. Add a scene as a layer in another scene.
6. Select, move, resize, crop, rotate, reorder, hide, lock, and adjust opacity for layers.
7. Configure supported layer and scene effects through typed controls.
8. Switch explicitly between Draft and Live editing.
9. Apply or discard Draft changes.
10. Configure a native preview output.
11. Configure MP4 and RTMP outputs with masked secrets and concrete unavailable reasons.
12. Route a scene to an output with Cut or Fade.
13. Start/stop preview, recording, and streaming through real engine services.
14. Inspect actionable state, drops, reconnects, elapsed time, and failure reason.
15. Save atomically without losing canonical fields that the current UI cannot edit.
16. Close or replace the project with deterministic draft, output, subscription, timer, engine, and resource cleanup.

## Required Studio deliverables

- Native hosted GPU preview below the Avalonia editor overlay.
- Platform-neutral `IHostedPreviewSurface` contract and Windows implementation in the platform Studio project.
- Attach, resize/DPI, rebind, detach, timeout, and close ownership tests.
- Source dialogs that edit canonical typed settings instead of creating placeholder definitions.
- Output dialogs that edit canonical typed settings and never store runtime secrets in project JSON.
- Real output cards for preview, MP4, and RTMP with capability truth and runtime state.
- Scene-as-source workflow in the source/layer UI.
- Real Draft/Live state driven by engine sessions.
- Visual QA at 1366x768, 1920x1080, and 2560x1440.
- Accessibility names and stable automation ids for the primary workflow.

## Studio acceptance criteria

- The entire required user workflow completes without a fake service bundle.
- The production bootstrap never falls back to design data after runtime failure.
- The canvas overlay remains responsive while the native surface is presenting.
- Preview resize and panel docking do not leak or prematurely destroy in-flight GPU resources.
- A rejected Live mutation preserves the last valid published scene and displays the reason.
- Apply marks only engine-reported affected outputs.
- Recording recovery starts a new numbered segment instead of overwriting an active file.
- RTMP failure does not stop MP4 recording.
- Project replacement awaits runtime draft disposal before clearing Studio session maps.
- Headless UI, visual QA, portable tests, Windows tests, Fast gate, and required GPU qualification pass.

# Execution plan

Each item below is one bounded implementation unit with tests, documentation, and a focused commit. Do not combine unrelated units merely to reduce commit count.

## Phase 1 — freeze the milestone and remove ambiguity

1. **Documentation alignment**
   - Align roadmap, limitations, Studio plan, README, contributor guidance, and agent instructions.
   - Acceptance: no normative document contradicts current RenderGraph or portable-audio reality.

2. **Public API surface audit**
   - Confirm that the quickstart workflow can be expressed without internal types.
   - Remove or replace dangerous public shortcuts instead of preserving them for compatibility.
   - Acceptance: public API guard tests and documentation pass.

3. **API quickstart sample**
   - Add the canonical sample with nested canvas, Live, Apply, preview configuration, MP4, and RTMP capability handling.
   - Acceptance: sample builds on Windows; its project-authoring portion builds and is tested on Linux.

## Phase 2 — close the rendering and preview vertical

4. **Physical RenderGraph authority audit**
   - Make source acquisition, effect intermediates, canvas/output passes, fan-out, encoded dispatch, and temporary-resource ownership exclusively graph-driven in production.
   - Acceptance: production Vulkan submission rejects incomplete or divergent plans before import/recording.

5. **Hosted preview contract**
   - Introduce or finalize the platform-neutral hosted-surface lifecycle contract.
   - Acceptance: attach/resize/rebind/detach state machine has portable ownership tests.

6. **Windows hosted preview implementation**
   - Bind the Avalonia native host to the GPU presenter without CPU readback.
   - Acceptance: resize, DPI, dock/undock, minimize/restore, timeout, close, and repeated attach/detach tests pass.

7. **Preview sustained qualification**
   - Run the 30-minute preview workload and enforce baseline return.
   - Acceptance: stored report identifies adapter/driver and all required counters.

## Phase 3 — make API outputs operational

8. **API output routing completion**
   - Ensure preview, MP4, and RTMP activation is available through product-level APIs and capability snapshots.
   - Acceptance: no host manually wires internal encoders, exporters, or sink workers.

9. **Shared MP4+RTMP qualification**
   - Sustain one compatible render/convert/encode group with independent sinks.
   - Acceptance: recording has no silent drops; RTMP reports every drop/reconnect; failures remain isolated.

10. **API Live/Apply physical qualification**
    - Exercise nested scenes and active outputs during Live and Apply edits.
    - Acceptance: affected outputs, transitions, pins, submissions, and resource counters remain correct and bounded.

## Phase 4 — replace remaining Studio placeholders

11. **Canonical source editing**
    - Replace placeholder source creation with typed canonical definitions and capability-aware fields.
    - Acceptance: source save/open round-trip preserves every supported setting.

12. **Canonical output editing**
    - Replace placeholder output configuration with typed preview/MP4/RTMP settings and secret-safe runtime configuration.
    - Acceptance: disabled and unavailable outputs remain editable and persist correctly.

13. **Scene-as-source Studio workflow**
    - Add explicit canvas selection as a layer source with cycle/depth validation.
    - Acceptance: nested scene appears, edits propagate according to binding, and invalid graphs are rejected visibly.

14. **Studio engine vertical completion**
    - Connect preview, output cards, Draft/Live editing, health, and shutdown to real services only.
    - Acceptance: the production workflow completes without fake services.

15. **Studio acceptance and polish**
    - Execute visual QA, accessibility, keyboard, docking, project replacement, and failure-state scenarios.
    - Acceptance: all Studio checklist items in milestone scope are green.

## Phase 5 — milestone gate

16. **Functional milestone report**
    - Run Windows/Linux baseline, Fast, GPU, media transport, license, Studio visual QA, hardware-media readiness, and 30-minute workloads.
    - Publish a single report that lists passed, unavailable, blocked, and deferred capabilities with reasons.
    - Acceptance: API and Studio workflows pass on the qualified Windows adapter; Linux portable validation remains green.

# Definition of done

The milestone is complete only when:

- the API quickstart and Studio execute the same production engine architecture;
- preview, MP4, RTMP, Live, Apply, nested canvas, capability truth, and deterministic shutdown are demonstrated together;
- Windows and Linux baseline CI pass;
- required physical evidence is stored and reviewed;
- no product capability depends on fake services, CPU video fallback, contract-only native libraries, or undocumented manual wiring;
- documentation describes the implementation that actually exists.
