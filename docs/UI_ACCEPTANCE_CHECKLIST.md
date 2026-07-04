# Studio UI Acceptance Checklist

Use this checklist when reviewing the first Avalonia implementation of WTK MediaForge Studio.

## Scope Control

- [ ] The UI is implemented in Avalonia UI.
- [ ] No React, WebView, Electron, Tailwind runtime, or browser dependency was added.
- [ ] The Lovable prototype was used as a reference only.
- [ ] No real source adapter was implemented as part of the UI shell.
- [ ] No real recording/streaming/NDI/virtual-camera sink was implemented as part of the UI shell.
- [ ] No real audio pipeline was implemented.
- [ ] No legacy preview/capture path was revived.

## Build

- [ ] `dotnet build` passes.
- [ ] `./scripts/test.ps1 -Tier Fast` passes.
- [ ] No GPU tests are required unless render/capture/GPU code changed.

## Shell Layout

- [ ] Title bar exists.
- [ ] Toolbar exists.
- [ ] Project Explorer exists on the left.
- [ ] Preview/canvas area exists in the center.
- [ ] Inspector exists on the right.
- [ ] Bottom Workbench exists below preview.
- [ ] Status bar exists at the bottom.
- [ ] Window can be resized without major layout breakage.
- [ ] No large unexplained empty area appears below the status bar.

## Theme

- [ ] Dark theme is centralized in resources.
- [ ] Brushes use semantic names.
- [ ] Active, hover, selected, disabled, success, warning, error, recording, and planned states are visually distinct.
- [ ] Text contrast is acceptable on dark surfaces.
- [ ] Monospace/tabular text is used for telemetry where practical.

## Project Explorer

- [ ] Scenes group appears.
- [ ] Sources group appears.
- [ ] Outputs group appears.
- [ ] Presets group appears.
- [ ] Packages group appears.
- [ ] Rows support icon, label, metadata, badge/dot.
- [ ] Active/selected item is visible.
- [ ] Selecting items updates shell selection.

## Preview Canvas

- [ ] Preview header shows scene/canvas metadata.
- [ ] Zoom/grid/safe controls are visible.
- [ ] Checker background is visible around the canvas.
- [ ] 16:9 fake canvas is visible.
- [ ] Selected layer overlay is visible.
- [ ] Resize handles are visible.
- [ ] No real GPU/native preview is required in v0.1.

## Inspector

- [ ] Inspector changes based on selected item kind.
- [ ] Layer inspector shows Transform, Crop, Effects.
- [ ] Source inspector shows Device and Status.
- [ ] Scene inspector shows Canvas and linked outputs.
- [ ] Output inspector shows Destination, Encoder, Health.
- [ ] Secret fields, such as stream key, are masked by default.
- [ ] Inspector is MVVM/data-template driven, not code-behind driven.

## Bottom Workbench

- [ ] Layers tab exists and shows rows.
- [ ] Effects tab exists and shows Chroma Key and Blur examples.
- [ ] Timeline tab exists as placeholder.
- [ ] Diagnostics tab exists and shows fake logs.
- [ ] Performance tab exists and shows metric cards and sparkline/chart placeholder.
- [ ] Output Monitor tab exists and shows output status rows.
- [ ] Audio Mixer tab exists as `BETA` / future placeholder.

## Commands

- [ ] New command exists.
- [ ] Open command exists.
- [ ] Save command exists.
- [ ] Add Source command exists.
- [ ] Add Scene command exists.
- [ ] Start/Stop Engine command toggles fake state.
- [ ] Start Streaming command has a visual state, even if fake/disabled.
- [ ] Start Recording command has a visual state, even if fake/disabled.
- [ ] Settings command exists or is visibly planned.
- [ ] Button enabled state is controlled by command `CanExecute` or bound ViewModel state.

## MVVM

- [ ] Views use compiled bindings where practical.
- [ ] Views declare `x:DataType` where practical.
- [ ] ViewModels do not reference Avalonia controls.
- [ ] Product logic is not placed in `.axaml.cs`.
- [ ] Design/mock data is isolated from views.
- [ ] Lists use observable collections where mutability is expected.

## Documentation

- [ ] `docs/UI_STUDIO_DESIGN.md` remains current with the implementation.
- [ ] `docs/UI_REACT_TO_AVALONIA_MAPPING.md` remains current if the implementation deviates intentionally.
- [ ] `docs/UI_IMPLEMENTATION_PLAN.md` is updated when implementation phases change.
- [ ] Any UI/runtime boundary decision is reflected in `docs/AI_CONTEXT.md` or `docs/ROADMAP_CURRENT.md`.

