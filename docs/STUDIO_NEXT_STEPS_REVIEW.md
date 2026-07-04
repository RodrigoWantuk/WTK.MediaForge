# WTK MediaForge Studio Next Steps Review

Review date: 2026-07-04

## Current Status

`WTK.MediaForge.Studio` now contains a real Avalonia mock shell. It is a UI/product workflow surface only: it has no real GPU preview, capture adapter, encoder, NDI, RTSP, streaming, virtual camera, or audio integration.

Implemented baseline:

- dark Avalonia workbench shell;
- Project Explorer, preview mock, contextual Inspector, Bottom Workbench, Status Bar;
- fake engine, stream, recording, selection, layer, effect, diagnostics, performance, output monitor, and future audio states;
- constructor-injected fake Studio services;
- unified selection state;
- typed engine/output UI states;
- typed layer inspector fields;
- Studio ViewModel tests included in the Fast tier.

## Immediate UI Track

1. Keep the native OS title bar for now. The internal Studio row is an app header, not custom window chrome.
2. Continue polishing the mock shell while it remains runtime-free.
3. Keep Studio services as the boundary between ViewModels and future engine/project/runtime integrations.
4. Move toward model-backed project create/open/save only through controlled project services.
5. Do not attach real preview until the `PreviewPanelSink` reliability gate is complete.

## Runtime Gates

Still blocked until the roadmap opens them:

- real media/source adapters;
- real encoder/streaming/NDI/virtual-camera outputs;
- real audio capture, mix, mux, or equalization;
- productive Studio preview integration beyond the approved preview reliability path.

## Next Milestones

- Studio Shell v0.1 stabilized: layout, state, test gate, service boundaries.
- Studio Architecture v0.2: service-backed project state, unified selection, typed inspector validation.
- Project Workflow v0.3: real project new/open/save through existing product model contracts.
- Engine Control v0.4: safe engine bridge without real preview.
- Preview MVP v0.5: native preview surface only after reliability criteria pass.
