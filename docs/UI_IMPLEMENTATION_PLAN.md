# Avalonia Studio Implementation Plan

This plan tracks the current v13 Studio integration of `WTK.MediaForge.Studio`.

## Current Goal

Build a usable native Avalonia/MVVM editor around the product model:

```text
Project -> Scenes -> Layers -> Outputs
             ^          ^
             |          |
          Sources   Effects in context
```

Studio remains runnable when hardware capabilities are unavailable. Runtime
features are disabled with a reason instead of replaced by fake success.

## Implemented Direction

- `StudioDocument` is the editable Studio projection of the canonical engine project.
- Production bootstrap is distinct from Design/Test bootstrap. Production owns
  a Windows engine session, canonical `MediaForgeProject` persistence, and an
  asynchronous cached capability probe.
- New projects start empty. Open/save validate and atomically write canonical
  engine JSON; runtime leases and native handles are never serialized.
- Engine sources and outputs are projected as known-editable, known-read-only,
  or opaque. A save without edits preserves canonical settings, advanced effect
  parameters, transform pivots, disabled outputs, and definitions with no
  Studio editor.
- `CurrentScene` drives the canvas, layer table, scene outputs, and properties.
- The primary left panel lists only scenes.
- Sources are global and reusable, but are added through the source library
  dialog instead of the primary scene list.
- Layers are scene-scoped and selectable/editable from the canvas, properties
  panel, and layer table.
- Layer effects are embedded in layer properties.
- Scene effects are embedded in scene properties.
- Outputs have `AssignedSceneId`, `DefaultTransitionId`,
  `TransitionDurationMs`, `IsEnabled`, `IsConfigured`, `IsLive`, and
  `IsRecording`.
- Output routing uses `SendSceneToOutput(outputId, sceneId, transitionId,
  durationMs)` and production cards, not a loose scene combo box.
- Source, scene, output, and route-output workflows are described by
  `IStudioDialogService` requests. The shell only applies those typed requests
  to the current overlay and wires commands, which keeps capability/routing
  dialog construction out of `StudioShellViewModel`.
- Streaming/recording buttons depend on configured output routes, not an engine
  toggle.
- The bottom workbench contains only `Camadas` and `Saídas da cena`.
- `SceneViewportState` owns deterministic pan/zoom math.
- Scene draft editing now has bounded undo/redo history through
  `IStudioUndoRedoService`; Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y, project shortcuts, and
  canvas zoom shortcuts resolve through `IStudioShortcutService`.
- Apply draft synchronization computes a deterministic layer diff and submits
  one atomic mutation batch. Unchanged 100-layer scenes submit no mutations;
  single-property edits submit only their required patch.
- `StudioShellViewModel` owns the UI engine lifecycle and real health/status
  subscriptions. Start, Stop, and Restart command availability follows engine
  state; project replacement and application shutdown stop new work and unwind
  drafts, outputs, timer, subscriptions, and engine deterministically.
- Dock panel proportions are loaded before Dock creation and persisted on
  window close/settings save through `IStudioLayoutService`.
- Diagnostics, performance metrics, and output monitor snapshots live in the
  Settings `Avançado` surface instead of the main production workspace.
- The toolbar exposes icon-only undo/redo affordances backed by the same
  commands as keyboard shortcuts.
- The main UI uses pt-BR terminology and avoids engine/debug language.
- `StudioVisualQaService` and `scripts/verify-studio-ui-visual-qa.ps1` validate
  the Studio shell contract at 1366x768, 1920x1080, and 2560x1440.
- `StudioAppSmokeTests` load `MainWindow` under Avalonia Headless, exercising
  XAML, resources, bindings, and the root shell ViewModel.
- Primary toolbar, project navigation, production outputs, bottom workbench,
  and canvas editor expose stable automation ids and accessible names.

## Required Files/Concepts

Key implementation areas:

- `StudioShellViewModel`: document ownership, scene selection, routing dialogs,
  command state;
- `ProductionPanelView`: output cards, routed scene, transition, state, and
  send-scene workflow;
- `PreviewCanvasViewModel`, `SceneViewportState`, and `StudioCanvasEditor`:
  zoom, pan, hit-test, move, resize, nudge;
- inspector ViewModels/views: contextual `Propriedades` pages;
- bottom panels: scene layers and outputs using the selected scene;
- localization resources and display-name service.

## Constraints

- No WebView, React, Electron, Tailwind runtime, or browser dependency.
- Studio does not instantiate adapters directly; it uses the engine/runtime boundary.
- No output is presented as active unless the runtime capability and route are real.
- No real audio capture/mix/mux/equalization.
- No real GPU preview integration until the preview reliability gate allows it.
- No legacy direct preview/capture path.
- ViewModels must not depend on Avalonia controls.
- Product logic must not live in `.axaml.cs`; code-behind is allowed only for
  visual pointer/keyboard behavior.

## Next UI Work

1. Preserve the headless nonblank screenshot regression at 1366x768,
   1920x1080, and 2560x1440 while adding platform image baselines only after
   font/rendering variance is normalized.
2. Keep the automated Studio visual QA gate green whenever layout, shell,
   preview editor, properties, workbench, accessibility, or shell-loading
   behavior changes.
3. After runtime gates: introduce the real preview frame provider below the
   Avalonia overlay.

## Validation

Run after UI changes:

```powershell
dotnet build
dotnet test
./scripts/test.ps1 -Tier Fast
./scripts/verify-studio-ui-visual-qa.ps1
```

GPU tier is not required for UI-only mock changes.
