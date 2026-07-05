# React Reference To Avalonia Mapping

The UI reference images are visual guidance only. Studio remains native
Avalonia/C#/MVVM. Do not embed React, WebView, Electron, Vite, Tailwind runtime,
or browser dependencies.

## Mapping

| Product concept | Avalonia implementation |
|---|---|
| App/workspace shell | `MainWindow`, `StudioShellViewModel` |
| Project navigation | `ProjectExplorerView`, `ProjectTreeGroupViewModel`, `ProjectTreeItemViewModel` |
| Editable preview/canvas | `StudioCanvasEditor`, `PreviewCanvasViewModel` |
| Layer rows | `LayersPanelView`, `LayerItemViewModel` |
| Contextual effects | `EffectsPanelView`, selected layer `EffectItemViewModel`s |
| Output routing | `OutputMonitorPanelView`, `OutputMonitorItemViewModel`, `StudioOutput.AssignedSceneId` |
| Right properties panel | `InspectorView` hosting contextual `*InspectorViewModel` pages |
| Toolbar actions | `RelayCommand` / `AsyncRelayCommand` on `StudioShellViewModel` |
| Mock dialogs | `StudioDialogViewModel` hosted by `MainWindow` overlay |

## Rules

- Treat `Scene` as the user-facing name for a canvas.
- Keep sources separate from layers.
- Keep outputs separate from sinks/runtime workers.
- Keep the Avalonia overlay separate from future native/GPU preview hosting.
- Do not expose engine/runtime internals in the main UI.
- Advanced diagnostics/performance views are future surfaces, not main tabs.
