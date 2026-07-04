# Studio Execution Plan vNext

This document is the canonical ordered execution plan for the next WTK MediaForge Studio phase. It keeps the Avalonia product shell moving without violating the runtime roadmap.

## Fixed Decisions

- Studio remains native Avalonia UI.
- Lovable/React references are visual only.
- No WebView, React, Electron, Tauri, Node, Tailwind runtime, or embedded browser is allowed.
- Studio remains usable in mock/design mode until runtime gates open.
- ViewModels must not reference Avalonia controls.
- Engine access must go through Studio services.
- Real preview remains blocked until the `PreviewPanelSink` reliability gate passes.
- Native OS title bar remains active. The internal Studio row is an app header, not custom chrome.

## Ordered Commits

1. UI list interaction fix.
2. Observable bindings and toolbar states.
3. Product text cleanup and responsive layout.
4. Workspace document model.
5. Typed selection and model-backed inspectors.
6. Project serialization.
7. Add Scene/Add Source/Add Output dialogs.
8. Data-driven canvas mock.
9. Effects and output monitor model-backed.
10. Engine service bridge without real preview.
11. Preview reliability harness.
12. Studio preview surface host after reliability gate.

## Explicit Blocks

Do not implement these before their owning gates open:

- real webcam;
- real RTMP;
- real MP4 encode;
- real NDI;
- real RTSP;
- real virtual camera;
- real audio;
- custom title bar;
- auto-update;
- public plugin API.

## Current vNext Slice

The active slice is commits 1 through 3:

- remove selectable row `Button` wrappers and avoid nested buttons;
- make preview header bindings observable;
- add typed toolbar state, busy state, and recording elapsed state;
- normalize product-facing state text;
- keep Studio tests in the Fast tier;
- document required resize checks.

The next slice starts with `StudioWorkspaceDocument` and model-backed project state.
