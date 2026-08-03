# Studio Acceptance Checklist

Use this checklist for the current Avalonia Studio functional milestone defined in [`MVP_API_STUDIO.md`](MVP_API_STUDIO.md).

A checked item must have current implementation and evidence. Do not check an item from design data, fake services, nominal capability, or an unexecuted test.

## Architecture and scope

- [ ] Avalonia UI has explicit Design/Test and Runtime composition.
- [ ] No React, WebView, Electron, Tailwind runtime, embedded browser, or competing frontend runtime exists.
- [ ] ViewModels do not depend on Avalonia controls.
- [ ] Product behavior does not live in `.axaml.cs` beyond pointer, keyboard, native-host, and window mechanics.
- [ ] Studio accesses media behavior through public engine/runtime contracts.
- [ ] Production runtime failure never falls back to fake/design services.
- [ ] `MediaForgeProject` is the sole persisted product model.
- [ ] Unavailable features expose a concrete reason.
- [ ] Runtime secrets do not enter project JSON.
- [ ] No legacy preview/capture product path is used.

## Project lifecycle

- [ ] New creates an empty canonical project.
- [ ] Open loads and validates canonical project JSON.
- [ ] Save validates a detached clone before writing.
- [ ] Save writes a temporary file and atomically replaces the destination.
- [ ] In-memory canonical session advances only after replacement succeeds.
- [ ] Fields not represented by current editors remain intact.
- [ ] Project replacement awaits all active runtime draft disposal.
- [ ] Project replacement stops outputs and presenter ownership before clearing session maps.
- [ ] Application close unwinds timer, subscriptions, edit sessions, outputs, presenter, engine, and resources in deterministic order.

## Capability and engine lifecycle

- [ ] Capability probing is asynchronous and never blocks the UI thread.
- [ ] Adapter/device generation changes invalidate the cached capability snapshot correctly.
- [ ] Engine state reflects Starting, Running, Degraded, Recovering, Failed, Stopping, and Stopped where applicable.
- [ ] Start, Stop, and Restart command availability follows real engine state.
- [ ] Unsupported hardware remains visible only when useful and is disabled with a concrete reason.
- [ ] Studio does not report a feature available from model presence or prototype evidence.

## Navigation and selection

- [ ] The primary left panel is scenes-first.
- [ ] Selecting a scene updates canvas, layers, scene outputs, and scene properties.
- [ ] Selecting a scene clears incompatible layer/source/output selection.
- [ ] Reusable sources are added through a source library.
- [ ] Layers are scoped to the current scene.
- [ ] Canvas, layer list, and properties selection remain synchronized.
- [ ] Selecting empty canvas space clears the layer selection.

## Canonical source workflow

- [ ] Static image source editor uses typed canonical settings.
- [ ] Desktop capture source editor uses typed canonical settings and capability reason.
- [ ] Window capture source editor uses typed canonical settings and capability reason.
- [ ] Webcam source editor uses typed canonical settings and capability reason.
- [ ] MP4 video-file source editor uses typed canonical settings and capability reason.
- [ ] Invalid source settings are rejected before canonical commit.
- [ ] Source round-trip preserves all supported settings and unknown extension data.
- [ ] No source dialog creates fake runtime availability.

## Canvas-as-source

- [ ] A user can add an existing scene as a layer in another scene.
- [ ] Direct cycles are rejected visibly.
- [ ] Transitive cycles are rejected visibly.
- [ ] Maximum nesting depth is enforced visibly.
- [ ] Nested scene identity and binding appear in layer properties.
- [ ] Published binding reflects published child changes.
- [ ] Draft binding remains isolated to the owning edit session.
- [ ] Apply propagation uses engine-reported affected outputs.

## Canvas editor

- [ ] Viewport math is centralized in `SceneViewportState` or its current canonical equivalent.
- [ ] Fit centers the scene.
- [ ] Mouse-wheel zoom preserves the point under the cursor.
- [ ] Button zoom uses viewport center.
- [ ] 100% zoom equals scale `1.0`.
- [ ] Topmost visible unlocked layer is selected by hit test.
- [ ] Drag moves unlocked layers.
- [ ] Resize handles resize unlocked layers.
- [ ] Locked layers cannot move or resize.
- [ ] Keyboard nudge works.
- [ ] Middle drag or Space+drag pans.
- [ ] Crop, rotation, opacity, visibility, lock, blend, and ordering use typed state.
- [ ] Grid and safe-area overlays remain Avalonia overlays.
- [ ] Overlay interaction remains responsive while native preview presents.

## Draft and Live editing

- [ ] Draft is the default editing mode.
- [ ] Draft changes do not alter published outputs before Apply.
- [ ] Apply submits one atomic diff batch.
- [ ] Unchanged scenes submit no mutations.
- [ ] Apply marks only engine-reported affected output ids.
- [ ] Discard physically closes the runtime draft session.
- [ ] Undo/redo is Draft-only and bounded.
- [ ] Entering Live with active outputs requires explicit confirmation.
- [ ] Live mode has a persistent strong visual indicator.
- [ ] Live pointer changes are coalesced to the latest atomic mutation per UI frame.
- [ ] Rejected Live mutation preserves the last valid published scene.
- [ ] Rejected Live mutation displays an actionable reason.
- [ ] Leaving Live flushes pending mutations and closes the runtime session deterministically.

## Hosted native preview

- [ ] Preview uses a GPU-backed completed output surface.
- [ ] Primary preview does not use `CpuReadbackSink`.
- [ ] Avalonia overlay is rendered independently above the native preview surface.
- [ ] Attach is asynchronous and capability-gated.
- [ ] Initial native size and DPI are applied.
- [ ] Repeated resize is bounded and leak-free.
- [ ] Native-handle rebind is supported.
- [ ] Dock/undock and panel movement preserve ownership.
- [ ] Minimize/restore preserves or recreates presenter state correctly.
- [ ] Timeout does not destroy in-flight resources.
- [ ] Detach is timeout-bounded.
- [ ] Window close during an in-flight frame is deterministic.
- [ ] Thirty-minute 1080p60 preview qualification passes.
- [ ] Resource counters return to baseline after preview stop.

## Properties

- [ ] Panel title and primary terminology are pt-BR.
- [ ] Scene properties include editable name, dimensions, FPS, background, effects, and linked outputs.
- [ ] Layer properties include position, dimensions, pivot, rotation, crop, opacity, blend, lock, visibility, and effects.
- [ ] Source properties include type-specific settings, capability, status, and add-to-scene behavior.
- [ ] Nested-canvas properties include target scene and version-binding information.
- [ ] Output properties include routed scene, transition, dimensions, codec profile, bitrate, GOP, destination, quality, and enablement.
- [ ] Secrets are masked by default.
- [ ] Output routing is an explicit send workflow, not a loose scene combo box.

## Output workflow

- [ ] Preview output uses real runtime activation.
- [ ] MP4 output uses real proof-gated runtime activation.
- [ ] RTMP output uses real proof-gated runtime activation.
- [ ] Disabled outputs persist without creating runtime routes.
- [ ] Unavailable outputs remain editable and show a reason.
- [ ] Cut and Fade are route transitions.
- [ ] Recording state includes elapsed time and current segment.
- [ ] Recording recovery starts a new numbered segment.
- [ ] Recording overflow/finalization failure is explicit and never a silent drop.
- [ ] RTMP state includes drops, reconnect attempts, and terminal reason.
- [ ] RTMP failure does not stop healthy MP4 recording.
- [ ] Compatible MP4+RTMP routes share render/conversion/encoder work.

## Main UI hygiene

- [ ] Preview/editor remains the dominant central surface.
- [ ] Primary workflow does not expose Vulkan, D3D11, fences, command buffers, or native handles.
- [ ] Diagnostics and low-level performance data remain in advanced surfaces.
- [ ] Main bottom workbench is limited to the approved production workflow.
- [ ] Main visible terminology is consistent pt-BR.
- [ ] Required accents render correctly.
- [ ] Normal and metadata text meet minimum readable sizes.
- [ ] Primary controls have stable automation ids.
- [ ] Primary controls have accessible names.
- [ ] Keyboard shortcuts resolve through the shortcut service.

## Automated validation

- [ ] `MainWindow` loads under Avalonia Headless with the production shell composition test.
- [ ] Headless screenshots at 1366x768, 1920x1080, and 2560x1440 are nonblank and structurally valid.
- [ ] Project/session round-trip tests pass.
- [ ] Draft/Live engine-service tests pass.
- [ ] Canvas editor geometry tests pass.
- [ ] Hosted preview lifecycle tests pass.
- [ ] Output state/isolation tests pass.
- [ ] Accessibility/automation-id tests pass.
- [ ] Windows and Linux portable CI jobs pass.
- [ ] Fast gate passes.
- [ ] Studio visual QA gate passes and its report is reviewed.
- [ ] GPU tier passes for preview/runtime/GPU changes.
- [ ] Required hardware-media readiness passes on the qualified adapter.

## Manual functional acceptance

- [ ] Create a project.
- [ ] Add a supported physical or static source.
- [ ] Create two scenes.
- [ ] Nest one scene in the other.
- [ ] Edit transforms and effects.
- [ ] Apply a Draft change with preview active.
- [ ] Publish a Live change with preview active.
- [ ] Record MP4 where capability permits.
- [ ] Stream RTMP to the qualification endpoint where capability permits.
- [ ] Disconnect RTMP and verify recording isolation.
- [ ] Save, reopen, and verify canonical state.
- [ ] Replace the project and verify cleanup.
- [ ] Close the application during active preview/output and verify deterministic cleanup.
