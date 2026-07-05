# Studio UI Recovery Plan

WTK MediaForge Studio is currently in a UI-only recovery and productization track.
The current Avalonia shell is not considered a product-ready MVP. It is a mock
workbench that must become a usable editor before real media capture, output,
audio, or GPU preview integration is wired into the Studio app.

## Approved Reference

The primary visual direction is the polished Studio workbench reference in
`docs/UIReference/ref001.png`, with supporting references in `ref-app.png`,
`ref004.png`, `ref005.png`, `ref006.png`, and `ref009.png`.

The Avalonia implementation must remain native Avalonia/MVVM. React, WebView,
Electron, browser runtimes, and legacy direct preview/capture paths are not
allowed.

## Problems Being Corrected

- Preview/canvas was static and did not support selecting, moving, or resizing layers.
- Inspector panels were read-heavy label panels instead of editing surfaces.
- Project Explorer relied on text blocks like `SCN`, `CAM`, and `IMG` instead of icons.
- Main surfaces exposed too much low-level technical language.
- Shell strings were hard-coded and had no localization foundation.
- Data was split across mock panels instead of a shared editable document model.
- Bottom workbench panels looked more like debug lists than product tools.

## Recovery Gate

Do not implement real Studio webcam, desktop/window capture, media adapters,
RTMP/SRT, NDI, virtual camera, encoding, audio, or productive GPU preview wiring
until the UI recovery milestones are accepted.

Allowed during this gate:

- Avalonia dark-theme shell recovery.
- Mock/design Project Explorer, preview editor, Inspector, Bottom Workbench,
  diagnostics, performance, output monitor, and future audio placeholder.
- Studio-only document model, fake services, fake commands, localization,
  selection synchronization, and ViewModel tests.

Blocked during this gate:

- Real capture/media adapters.
- Real recording/streaming/NDI/virtual-camera sinks.
- Real audio capture/mix/mux/equalization.
- Real `PreviewPanelSink` integration in Studio before the runtime preview
  reliability milestone.

## Commit Order

1. Document the recovery gate and visual acceptance criteria.
2. Strengthen design tokens and component styles.
3. Replace text badge icons with native vector icons.
4. Add localization infrastructure and migrate shell strings incrementally.
5. Introduce a shared Studio document model for scenes, sources, layers, effects,
   outputs, presets, and packages.
6. Rebuild Project Explorer with icons, search, badges, status, and actions.
7. Replace the static preview with an editable canvas mock that supports layer
   selection, movement, resize handles, grid/safe overlays, and zoom controls.
8. Rebuild inspectors as editing panels with numeric fields, sliders, dropdowns,
   toggles, status cards, and masked secret fields.
9. Rework Bottom Workbench panels for layers, effects, diagnostics, performance,
   output monitor, and future audio placeholder.
10. Expand tests for selection, editing, command state, localization, and masking.

## Acceptance

The Studio shell is acceptable for the next UI stage only when:

- the app looks like a professional editor, not a debug prototype;
- the preview canvas can select, move, and resize mock layers;
- inspector edits update the same layer visible on the canvas and layers table;
- Project Explorer uses icons, search, clear hierarchy, and product status text;
- main screens hide low-level type ids and runtime internals;
- primary shell strings are externalized;
- tests cover editing and selection synchronization.
