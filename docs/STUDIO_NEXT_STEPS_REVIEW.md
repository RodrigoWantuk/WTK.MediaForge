# WTK MediaForge Studio Next Steps Review

Review date: 2026-07-05

## Current Status

`WTK.MediaForge.Studio` contains a native Avalonia mock editor. It is a
UI/product workflow surface only: no real GPU preview, capture adapter, encoder,
NDI, RTSP, streaming, virtual camera, or audio integration is wired.

Implemented baseline:

- dark Avalonia workbench shell;
- Project Explorer with scenes, sources, outputs, presets, and packages;
- scene-scoped editable canvas mock;
- contextual `Propriedades` pages;
- bottom `Camadas`, `Efeitos`, and `Saidas` workbench;
- output routing by assigned scene;
- stream/record mock state based on configured outputs;
- constructor-injected fake Studio services;
- unified selection state;
- typed output UI state;
- Studio ViewModel tests in the Fast tier.

## Immediate UI Track

1. Keep the native OS title bar for now.
2. Polish the mock editor against `docs/UIReference/ref001.png`.
3. Keep Studio services as the future boundary to project/runtime integration.
4. Add stronger command/dialog abstractions for source, scene, and output setup.
5. Add undo/redo contracts before destructive editing.
6. Keep advanced diagnostics/performance outside the main bottom workbench.
7. Do not attach real preview until the `PreviewPanelSink` reliability gate is
   complete.

## Runtime Gates

Still blocked until the roadmap opens them:

- real media/source adapters;
- real encoder/streaming/NDI/virtual-camera outputs;
- real audio capture, mix, mux, or equalization;
- productive Studio preview integration beyond the approved preview reliability
  path.

## Next Milestones

- Studio v0.1 polish: visual refinement, interactions, dialogs, screenshots.
- Project workflow v0.2: real project new/open/save through product model
  contracts.
- Advanced tools v0.3: diagnostics/performance surfaces behind explicit menu.
- Runtime bridge v0.4: status/diagnostics bridge without productive preview.
- Preview MVP v0.5: native preview surface only after reliability criteria pass.
