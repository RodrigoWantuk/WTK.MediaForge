# Avalonia Studio Implementation Plan

This plan tracks the current v0.1 overhaul of `WTK.MediaForge.Studio`.

## Current Goal

Build a usable native Avalonia/MVVM mock editor around the product model:

```text
Project -> Scenes/Canvas -> Sources -> Layers -> Outputs/Sinks
```

The goal is product UX correctness before runtime integration. The Studio app
must remain runnable without GPU, capture devices, encoders, streaming, NDI, or
audio.

## Implemented Direction

- `StudioDocument` is the shared mock document.
- `CurrentScene` drives the canvas, layer table, and scene properties.
- Sources are global and can be added to the current scene as layers.
- Layers are scene-scoped and are selectable/editable from the canvas,
  properties panel, and layer table.
- Effects are contextual to the selected layer.
- Outputs have `AssignedSceneId`, `IsEnabled`, and `IsConfigured`.
- Streaming/recording buttons depend on configured output routes, not an engine
  toggle.
- The main bottom workbench contains only `Camadas`, `Efeitos`, and `Saidas`.
- The main UI uses pt-BR terminology and avoids engine/debug language.

## Required Files/Concepts

Key implementation areas:

- `StudioShellViewModel`: document ownership, scene selection, routing, dialogs,
  command state;
- `PreviewCanvasViewModel` and `StudioCanvasEditor`: zoom, pan, hit-test,
  move, resize, nudge;
- inspector ViewModels/views: contextual `Propriedades` pages;
- bottom panels: scene layers, selected-layer effects, output routes;
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

1. Improve visual polish against `docs/UIReference/ref001.png`.
2. Add keyboard shortcut service and undo/redo command contracts.
3. Add real dialog service abstractions for add source/scene/output.
4. Add panel size persistence.
5. Add advanced diagnostics/performance views behind an explicit menu.
6. After runtime gates: introduce a preview host under the Avalonia overlay.

## Validation

Run after UI changes:

```powershell
dotnet build
dotnet test
./scripts/test.ps1 -Tier Fast
```

GPU tier is not required for UI-only mock changes.
