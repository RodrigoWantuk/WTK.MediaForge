# Studio Execution Plan vNext

This document is the canonical ordered execution plan for the next WTK MediaForge Studio phase. It keeps the Avalonia product shell moving without violating the runtime roadmap.

## Fixed Decisions

- Studio remains native Avalonia UI.
- Lovable/React references are visual only.
- No WebView, React, Electron, Tauri, Node, Tailwind runtime, or embedded browser is allowed.
- Studio remains usable in mock/design mode until runtime gates open.
- ViewModels must not reference Avalonia controls.
- Engine access must go through Studio services.
- Real preview remains blocked until the `PreviewPanelSink` reliability gate passes.
- Native OS title bar remains active. The internal Studio row is an app header, not custom chrome.

## Ordered Commits

1. UI list interaction fix.
2. Observable bindings and toolbar states.
3. Product text cleanup and responsive layout.
4. Workspace document model.
5. Typed selection and model-backed inspectors.
6. Project serialization.
7. Add Scene/Add Source/Add Output dialogs.
8. Data-driven canvas mock.
9. Effects and output monitor model-backed.
10. Engine service bridge without real preview. The foundation exists:
    deterministic Studio-to-engine ids, StudioDocument-to-MediaForgeProject
    mapping, StudioLayer-to-SceneMutationPatch mapping, and an async
    StudioSceneEditBridge over the engine Live/Apply contract.
11. Dialog workflow service boundary for source/scene/output/routing dialogs.
12. Preview reliability harness.
13. Studio preview surface host after reliability gate.

## Explicit Blocks

Do not implement these before their owning gates open:

- real webcam;
- real RTMP;
- real MP4 encode;
- real NDI;
- real RTSP;
- real virtual camera;
- real audio;
- custom title bar;
- auto-update;
- public plugin API.

## Current vNext Slice

The active slice has advanced through the engine bridge foundation:

- source/output dialogs are capability-driven instead of hardcoded available;
- the mock scene editor supports rotated layer hit-test and visual handles;
- `StudioProjectEngineMapper` creates a validated engine project from the
  Studio document model;
- `StudioSceneMutationFactory` creates real engine mutation patches for layer
  transform, bounds, visibility, opacity, and supported layer effects;
- `StudioSceneEditBridge` wraps the engine Live/Apply session contract without
  forcing a real preview path.

The current slice has wired shell edit commands to the async bridge through
Studio services, preserving mock/design mode and never blocking the UI thread.
Dialog construction now lives behind `IStudioDialogService`, so the shell
applies typed dialog requests instead of owning source/output capability and
routing dialog assembly. The shell now also owns bounded scene draft undo/redo
and resolves keyboard shortcuts through `IStudioShortcutService`, while Avalonia
controls only map key events into Studio shortcut gestures. Dock proportions are
restored before Dock creation and persisted through `IStudioLayoutService`.
Diagnostics, output snapshots, and performance mock metrics moved to the
Settings `Avançado` surface so the main workspace stays focused on scenes,
layers, sources, and routed outputs. The toolbar now exposes localized
icon-only undo/redo controls backed by the same scene draft commands as
keyboard shortcuts.

The next UI slice is visual QA at the target viewports. The real preview frame
provider remains gated by runtime readiness.
