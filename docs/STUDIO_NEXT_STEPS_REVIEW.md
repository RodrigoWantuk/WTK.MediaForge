# WTK MediaForge Studio Next Steps Review

Review date: 2026-07-20

## Current Status

`WTK.MediaForge.Studio` is a native Avalonia/MVVM mock editor. It is a
product-workflow shell over the MediaForge model and intentionally does not wire
real GPU preview, capture adapters, encoders, RTMP, NDI, virtual camera, or
audio.

Implemented v0.2 direction:

- dark Avalonia workbench shell with no classic top menu bar;
- left-side project navigation with scenes, reusable sources, and outputs kept
  in distinct tabs;
- source library and output setup dialogs behind `IStudioDialogService`;
- scene-scoped editable canvas with deterministic viewport math, hit-test,
  drag, nudge, pan, rotation-aware selection, and resize handles;
- right-side production/output cards with routed scene, state, and transition;
- explicit route workflow through `SendSceneToOutput(...)`;
- contextual `Propriedades` pages for scene, layer, source, and output;
- bottom workbench limited to `Camadas` and `Saídas da cena`;
- layer effects in layer properties and scene effects in scene properties;
- stream/record visual state driven by configured outputs, not engine state;
- constructor-injected Studio services and mock/design data;
- bounded scene draft undo/redo and keyboard shortcut routing;
- persisted dock layout through `IStudioLayoutService`;
- advanced diagnostics/performance/output snapshots in Settings, not the main
  workspace;
- Studio ViewModel, viewport, visual-QA, accessibility, and headless app smoke
  tests in the test suite.

## Current Gates

- `scripts/verify-studio-ui-visual-qa.ps1` validates the shell contract at
  1366x768, 1600x900, and 1920x1080 and writes
  `test-reports/studio-visual-qa-report.md`.
- `StudioAppSmokeTests` loads `MainWindow` under Avalonia Headless so XAML,
  resources, bindings, and root shell creation are exercised in CI-friendly
  tests.
- `StudioAccessibilityTests` keeps stable automation ids and accessible names
  on the primary interactive surfaces.

## Immediate UI Track

1. Keep the automated visual-QA and headless smoke gates green whenever shell,
   docking, canvas, properties, or workbench views change.
2. Continue manual polish against `docs/UIReference`: contrast, spacing,
   visual weight, icon consistency, and interaction feel.
3. Improve keyboard/focus behavior where manual review finds rough edges.
4. Keep advanced diagnostics/performance outside the main workspace.
5. Do not attach real preview until the runtime preview/provider gate opens.

## Runtime Gates

Still blocked from the Studio UI track until the engine roadmap opens them:

- real media/source adapters;
- real encoder/streaming/NDI/virtual-camera outputs;
- real audio capture, mix, mux, or equalization;
- productive Studio preview integration beyond the approved GPU preview path.

## Next Milestones

- Studio v0.2 polish: manual visual QA and accessibility refinements.
- Project workflow v0.3: product save/open/import/export experience over the
  serialization contracts.
- Runtime bridge v0.5: status/capability/diagnostics bridge without productive
  preview.
- Preview milestone v0.6: native preview surface only after runtime readiness.
