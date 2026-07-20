# Avalonia Studio Implementation Plan

This plan tracks the current v0.2 reset of `WTK.MediaForge.Studio`.

## Current Goal

Build a usable native Avalonia/MVVM mock editor around the product model:

```text
Project -> Scenes -> Layers -> Outputs
             ^          ^
             |          |
          Sources   Effects in context
```

The goal is product UX correctness before runtime integration. The Studio app
must remain runnable without GPU, capture devices, encoders, streaming, NDI, or
audio.

## Implemented Direction

- `StudioDocument` is the shared mock document.
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
- Dock panel proportions are loaded before Dock creation and persisted on
  window close/settings save through `IStudioLayoutService`.
- Diagnostics, performance metrics, and output monitor snapshots live in the
  Settings `Avançado` surface instead of the main production workspace.
- The toolbar exposes icon-only undo/redo affordances backed by the same
  commands as keyboard shortcuts.
- The main UI uses pt-BR terminology and avoids engine/debug language.
- `StudioVisualQaService` and `scripts/verify-studio-ui-visual-qa.ps1` validate
  the Studio shell contract at 1366x768, 1600x900, and 1920x1080.

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
- No real capture/media/source adapters.
- No real recording/streaming/NDI/virtual-camera sinks.
- No real audio capture/mix/mux/equalization.
- No real GPU preview integration until the preview reliability gate allows it.
- No legacy direct preview/capture path.
- ViewModels must not depend on Avalonia controls.
- Product logic must not live in `.axaml.cs`; code-behind is allowed only for
  visual pointer/keyboard behavior.

## Next UI Work

1. Keep the automated Studio visual QA gate green whenever layout, shell,
   preview editor, properties, or workbench views change.
2. After runtime gates: introduce the real preview frame provider below the
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
