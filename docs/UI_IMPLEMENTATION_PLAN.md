# Avalonia Studio Implementation Plan

This plan is for an AI/developer implementing the first WTK MediaForge Studio shell in Avalonia.

## Goal

Create a realistic dark-theme desktop shell that matches the approved Studio mockup and operates entirely from mock/design data at first. The shell prepares the product surface for the MediaForge pipeline without prematurely coupling to GPU preview, capture, encoders, NDI, RTSP, or audio.

## Pre-flight

Before implementation:

1. Read:
   - `docs/UI_STUDIO_DESIGN.md`
   - `docs/UI_REACT_TO_AVALONIA_MAPPING.md`
   - `docs/ROADMAP_CURRENT.md`
   - `docs/AI_CONTEXT.md`
2. Confirm `WTK.MediaForge.Studio` builds.
3. Confirm CommunityToolkit.Mvvm is referenced.
4. Confirm compiled bindings are enabled.
5. Keep all UI prototype work isolated to the Studio project unless adding shared abstractions is explicitly required.

## Mandatory Constraints

- Do not add WebView.
- Do not add React, Node, Tailwind, Vite, or browser runtime dependencies.
- Do not implement real capture/source adapters.
- Do not implement real output/encoder/streaming sinks.
- Do not implement real audio pipeline.
- Do not bypass the approved MediaForge runtime path.
- Do not use legacy preview/capture paths.
- Do not place business/product logic in `.axaml.cs`.
- Do not let ViewModels depend on Avalonia controls.

## Recommended Commit Sequence

### Commit 1: Theme and shell frame

Add:

```text
Styles/Theme.axaml
Styles/Controls.axaml
Views/MainWindow.axaml
Views/Shell/StudioTitleBarView.axaml
Views/Shell/StudioToolbarView.axaml
Views/Shell/ProjectExplorerView.axaml
Views/Shell/PreviewCanvasView.axaml
Views/Shell/BottomWorkbenchView.axaml
Views/Shell/InspectorView.axaml
Views/Shell/StudioStatusBarView.axaml
```

Acceptance:

- app starts;
- dark theme is applied;
- major regions are visible;
- no fake data required yet.

### Commit 2: Mock state and shell view models

Add:

```text
ViewModels/StudioShellViewModel.cs
ViewModels/TitleBarViewModel.cs
ViewModels/ToolbarViewModel.cs
ViewModels/ProjectExplorerViewModel.cs
ViewModels/PreviewCanvasViewModel.cs
ViewModels/BottomWorkbenchViewModel.cs
ViewModels/InspectorHostViewModel.cs
ViewModels/StatusBarViewModel.cs
DesignData/StudioDesignData.cs
```

Acceptance:

- project name shown;
- engine running/stopped fake state shown;
- FPS/drop/GPU fake metrics shown;
- Project Explorer populated;
- status bar populated.

### Commit 3: Project Explorer and selection

Implement:

- grouped explorer sections;
- items with icon kind, label, metadata, badge, health dot;
- selected item state;
- `SelectProjectItemCommand`.

Acceptance:

- selecting scene/source/output changes selected item;
- selected item updates visual state;
- inspector host receives selection.

### Commit 4: Contextual inspector pages

Add:

```text
Views/Inspectors/LayerInspectorView.axaml
Views/Inspectors/SourceInspectorView.axaml
Views/Inspectors/SceneInspectorView.axaml
Views/Inspectors/OutputInspectorView.axaml
ViewModels/Inspectors/*
```

Acceptance:

- layer selection shows transform/crop/effects;
- source selection shows device/status;
- scene selection shows canvas/linked outputs;
- output selection shows destination/encoder/health;
- stream key is masked by default.

### Commit 5: Bottom Workbench

Add tabs:

- Layers;
- Effects;
- Timeline placeholder;
- Diagnostics;
- Performance;
- Output Monitor;
- Audio Mixer placeholder.

Acceptance:

- tab switching works;
- Layers table populated;
- Effects card with Chroma Key expanded;
- Diagnostics shows fake logs;
- Performance shows metric cards and simple sparkline;
- Output Monitor shows output state table;
- Audio Mixer is clearly marked `BETA` / future.

### Commit 6: Canvas prototype

Implement fake canvas:

- checkerboard background;
- preview header and controls;
- fake 16:9 scene canvas;
- selected layer overlay bounds;
- resize handles as visual-only markers;
- optional fake render timing badge.

Acceptance:

- canvas resembles mockup;
- zoom/grid/safe toggles update visual state only;
- no real GPU preview yet.

### Commit 7: Command behavior

Implement fake commands:

- New;
- Open;
- Save;
- Add Source;
- Add Scene;
- Start/Stop Engine;
- Start Streaming;
- Start Recording;
- Settings.

Acceptance:

- commands bind through `ICommand`/CommunityToolkit;
- start/stop toggles fake engine state;
- buttons enable/disable from command state;
- diagnostics fake log receives command events where useful.

## ViewModel Guidelines

Use CommunityToolkit patterns:

```csharp
public partial class StudioShellViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isEngineRunning;

    [RelayCommand]
    private void ToggleEngine()
    {
        IsEngineRunning = !IsEngineRunning;
    }
}
```

For command enablement:

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(StartEngineCommand))]
[NotifyCanExecuteChangedFor(nameof(StopEngineCommand))]
private bool isEngineRunning;

private bool CanStartEngine() => !IsEngineRunning;
private bool CanStopEngine() => IsEngineRunning;
```

## Inspector Architecture

Preferred approach:

```text
InspectorHostViewModel.SelectedInspectorPage
  -> LayerInspectorViewModel
  -> SourceInspectorViewModel
  -> SceneInspectorViewModel
  -> OutputInspectorViewModel
```

Resolve views with data templates or the view locator.

Do not make `InspectorView` contain large `if/else` code-behind.

## Canvas Architecture

Start with a pure Avalonia visual canvas:

```text
PreviewCanvasView
  Checker background
  Canvas viewport
  Fake scene surface
  Overlay layer selection
  Handles
  Render timing label
```

Later, introduce a separate host for real preview:

```text
PreviewCanvasView
  PreviewSurfaceHostControl
    platform/native/GPU presentation implementation
  Overlay adorner layer
```

The overlay layer must remain separate from the native/GPU surface so selection handles, guides, and safe-area UI stay in Avalonia.

## Styling Guidelines

- Centralize all brushes.
- Centralize button styles.
- Use semantic style keys.
- Avoid hard-coded colors in individual views.
- Use consistent heights:
  - title bar: 36;
  - toolbar: 44;
  - panel header: 36;
  - compact buttons: 28-32;
  - status bar: 27.
- Prefer `Grid` over absolute positioning for shell layout.
- Use `Canvas` only inside the preview overlay layer where absolute layer handles make sense.

## Test/Validation Expectations

Minimum validation after UI commits:

```powershell
dotnet build
./scripts/test.ps1 -Tier Fast
```

If UI changes do not touch engine/render/capture/GPU code, GPU tests are not required.

Recommended tests:

- ViewModel selection tests;
- command enablement tests;
- inspector resolution tests;
- fake start/stop state transition tests;
- project explorer grouping tests.

Manual validation:

- app opens cleanly;
- resize window smaller/larger;
- tab switching works;
- selection changes inspector;
- fake engine toggle updates title/status/toolbar;
- theme remains consistent.

## Definition of Done for UI Shell v0.1

- It looks recognizably like the approved mockup.
- It is implemented in Avalonia, not web.
- It is MVVM-driven.
- It uses centralized dark theme resources.
- It runs without MediaForge engine integration.
- It creates no new runtime obligations.
- It gives future developers a stable visual and architectural base.

