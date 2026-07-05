# WTK MediaForge Studio Next Steps Review

Review date: 2026-07-05

## Current Status

`WTK.MediaForge.Studio` contains a native Avalonia mock editor. It is a
UI/product workflow surface only: no real GPU preview, capture adapter, encoder,
NDI, RTSP, streaming, virtual camera, or audio integration is wired.

Implemented v0.2 direction:

- dark Avalonia workbench shell with no classic top menu bar;
- left-side `Cenas` panel only;
- source library dialog opened by `+ Fonte`;
- scene-scoped editable canvas with deterministic viewport math;
- click, drag, nudge, pan, and resize handles for mock layers;
- right-side `Produção / Saídas` cards;
- explicit `SendSceneToOutput(...)` routing with output transition;
- contextual `Propriedades` pages;
- bottom `Camadas` and `Saídas da cena` workbench;
- stream/record state based on configured outputs;
- constructor-injected fake Studio services;
- unified selection state and typed output UI state;
- Studio ViewModel and viewport tests in the Fast tier.

## Immediate UI Track

1. Keep the native OS title bar for now.
2. Run visual QA at 1366x768, 1600x900, and 1920x1080.
3. Add stronger command/dialog abstractions for source, scene, and output setup.
4. Add undo/redo contracts before destructive editing.
5. Keep advanced diagnostics/performance outside the main workspace.
6. Do not attach real preview until the `PreviewPanelSink` reliability gate is
   complete.

## Runtime Gates

Still blocked until the roadmap opens them:

- real media/source adapters;
- real encoder/streaming/NDI/virtual-camera outputs;
- real audio capture, mix, mux, or equalization;
- productive Studio preview integration beyond the approved preview reliability
  path.

## Next Milestones

- Studio v0.2 visual QA: spacing, contrast, screenshots, resize behavior.
- Project workflow v0.3: real project new/open/save through product model
  contracts.
- Advanced tools v0.4: diagnostics/performance surfaces behind explicit mode.
- Runtime bridge v0.5: status/diagnostics bridge without productive preview.
- Preview MVP v0.6: native preview surface only after reliability criteria pass.
