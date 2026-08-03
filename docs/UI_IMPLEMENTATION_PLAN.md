# Avalonia Studio Implementation Plan

## Product role

WTK MediaForge Studio is the native Avalonia/MVVM product shell over the public MediaForge authoring, runtime, capability, scene-editing, preview, recording, and streaming contracts.

Studio does not own a second project model or a parallel media engine. `MediaForgeProject` remains canonical, and Studio projections must preserve valid data that the current UI cannot edit.

The active Studio delivery scope is defined together with the public API in [`MVP_API_STUDIO.md`](MVP_API_STUDIO.md).

## Non-negotiable constraints

- No React, WebView, Electron, browser runtime, or embedded web frontend.
- ViewModels do not depend on Avalonia controls.
- Product behavior does not live in `.axaml.cs`; code-behind is limited to visual pointer, keyboard, native-host, and window behavior.
- Studio does not instantiate platform media adapters, encoders, exporters, or sink workers directly.
- Production bootstrap does not fall back to fake/design services after runtime failure.
- Unavailable features are disabled with a concrete capability reason.
- The editor overlay remains separate from the native GPU preview surface.
- Primary preview, recording, and streaming never use continuous CPU readback.
- Runtime credentials and secrets never enter project JSON.
- Draft/Live semantics come from engine edit sessions.
- Project replacement and shutdown await physical draft/output/engine cleanup before clearing Studio maps.

## Current implementation

### Project and shell

- Native Avalonia application with explicit Design/Test and Runtime composition.
- `StudioDocument` projection over a canonical `MediaForgeProject` session.
- Empty new projects.
- Canonical open/save with validation, temporary-file write, atomic replacement, and commit-after-success semantics.
- Preservation of extension settings, typed source/output data, disabled outputs, encode profiles, advanced text/effect state, transform pivots, nested bindings, and opaque definitions covered by round-trip tests.
- Docking layout with persistence, reset, redock, and floating-panel state.
- Asynchronous capability probing.
- Engine lifecycle state, health subscriptions, start/stop/restart, project switch, and deterministic application shutdown.

### Scene editing

- Scenes-first navigation.
- Reusable global sources added through a source library.
- Scene-scoped layers.
- Canvas selection and layer-table synchronization.
- Move, resize, nudge, reorder, lock, visibility, zoom, pan, grid, and safe-area behavior.
- Contextual scene, source, layer, effect, and output projections.
- Bounded undo/redo.
- Explicit Draft and Live modes.
- Atomic draft diff submission.
- Apply and Discard through engine sessions.
- Live mutation coalescing, rejection reporting, and preservation of the last valid published scene.
- Apply completion scoped to engine-reported affected output ids.

### Outputs

- Production/output cards.
- Explicit scene-to-output routing with Cut/Fade transition selection.
- Real proof-gated MP4 and RTMP activation.
- Route state, failure detail, packet/drop/latency metrics, reconnect state, elapsed recording time, and numbered segment rollover.
- Disabled outputs remain canonical editable state without creating runtime routes.

### Quality

- Avalonia Headless application smoke tests.
- Stable automation ids and accessible names for primary controls.
- Visual QA at 1366x768, 1920x1080, and 2560x1440.
- Main workflow terminology in pt-BR.

## Remaining functional gaps

### 1. Hosted native preview

The primary gap is the real GPU preview hosted below the Avalonia editing overlay.

Required contract:

```text
Avalonia native host
  -> platform-neutral hosted-surface lifecycle
  -> Windows presenter binding
  -> completed GPU output lease
  -> native presentation
```

Required behavior:

- asynchronous attach;
- initial-size and DPI negotiation;
- repeated resize;
- native-handle rebind;
- dock/undock and panel movement;
- minimize/restore;
- timeout-bounded detach;
- close while a frame is in flight;
- presenter recovery without premature resource destruction;
- no continuous CPU readback.

Promotion requires the current hosted-preview proof and 30-minute sustained qualification.

### 2. Canonical source editors

Some source workflows still create simplified Studio definitions rather than complete typed canonical settings.

The milestone requires complete editors for the sources used by the functional workflow:

- static image;
- desktop capture;
- window capture;
- webcam;
- MP4 video file;
- text and solid primitives through their canonical layer models;
- canvas-as-source.

Each editor must:

- use stable ids;
- edit typed settings;
- show capability/status reason;
- preserve unknown canonical fields;
- validate before committing;
- never create a fake runtime source.

### 3. Canonical output editors

Complete typed editors are required for:

- hosted preview;
- MP4 recording;
- RTMP streaming.

Editors must cover routed scene, dimensions, frame rate, color/output configuration, H.264 profile/level, bitrate, GOP, destination, transition, enablement, and secret-safe runtime configuration.

Unavailable outputs remain editable but cannot activate.

### 4. Scene-as-source workflow

Studio must expose canvas-as-source explicitly rather than requiring file editing or internal test data.

Required behavior:

- choose an existing scene as a layer source;
- choose an allowed version binding where the workflow requires it;
- reject direct/transitive cycles;
- enforce nesting depth;
- show the nested scene in layer properties;
- propagate Live/Apply changes according to engine semantics;
- show affected output state from engine results only.

### 5. End-to-end production composition

The production application must complete the functional workflow without a fake service bundle:

```text
Open/Create project
  -> add source
  -> create scenes/layers/nested scene
  -> edit Draft/Live
  -> hosted preview
  -> route to MP4/RTMP
  -> observe health/failures
  -> save
  -> stop/replace/close deterministically
```

## Execution units

Each numbered unit is a focused implementation and review unit.

1. **Hosted-surface contract**
   - Finalize state, ownership, cancellation, timeout, and native-handle replacement semantics.
   - Tests: portable lifecycle state machine.

2. **Windows hosted presenter**
   - Connect the native Avalonia host to `PreviewPanelSink`/presenter through the platform Studio assembly.
   - Tests: attach, resize, DPI, rebind, detach, timeout, close.

3. **Studio preview integration**
   - Place the hosted surface below the existing overlay and keep hit-testing/editor geometry independent.
   - Tests: overlay interaction while preview runs, dock/undock, minimize/restore.

4. **Hosted preview qualification**
   - Run 30-minute 1080p60 preview and enforce resource baseline return.
   - Artifact: adapter/driver-specific report.

5. **Typed source library**
   - Replace placeholder source creation for milestone sources.
   - Tests: settings validation and canonical round-trip.

6. **Canvas-as-source UI**
   - Add nested scene selection, validation, and properties.
   - Tests: direct/transitive cycle rejection, depth, Live/Apply propagation.

7. **Typed output configuration**
   - Complete preview, MP4, and RTMP editors.
   - Tests: disabled/unavailable persistence, secret masking, capability gating.

8. **Production output cards**
   - Bind activation, stop, metrics, reconnect, failure, elapsed time, and segment rollover to real services.
   - Tests: MP4/RTMP isolation and state transitions.

9. **Production workflow closure**
   - Remove any remaining fake-service dependency from the runtime path.
   - Tests: complete workflow with canonical session, real engine service, and controlled test adapters where physical hardware is not part of the test.

10. **Studio acceptance**
    - Run headless, visual QA, keyboard, accessibility, docking, project replacement, failure, and shutdown scenarios.
    - Update `UI_ACCEPTANCE_CHECKLIST.md` with evidence references.

## Acceptance

Studio milestone acceptance requires all of the following:

- A user can create/open/save a canonical project.
- A user can configure a real supported source.
- A user can create two scenes and nest one inside the other.
- A user can move, resize, reorder, lock, hide, crop, rotate, and change opacity through typed state.
- Draft, Apply, Discard, and Live work through engine sessions.
- Hosted preview displays the current routed scene without CPU readback.
- MP4 and RTMP can be configured and activated when capabilities permit.
- Unavailable features show a concrete reason.
- RTMP failure does not stop recording.
- Recording recovery creates a new segment.
- Save/open preserves canonical fields not represented by the UI.
- Project replacement and application close unwind drafts, outputs, timers, subscriptions, engine, presenters, and resources in ownership order.
- Windows and Linux portable CI, Fast gate, Studio visual QA, and required GPU/hardware qualification pass.

## Validation

After every Studio implementation unit:

```powershell
dotnet build .\WTK.MediaForge.sln --configuration Release
dotnet test .\WTK.MediaForge.sln --configuration Release `
  --filter "Category!=GPU&Category!=Stress&Category!=Performance"
.\scripts\test.ps1 -Tier Fast
.\scripts\verify-studio-ui-visual-qa.ps1
```

When preview, runtime, Vulkan, D3D11, output, provider, or GPU ownership code changes:

```powershell
.\scripts\test.ps1 -Tier Gpu
.\scripts\verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Do not mark Studio preview or outputs available from UI-only tests.
