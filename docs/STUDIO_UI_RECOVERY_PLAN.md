# Studio UI Recovery Plan

WTK MediaForge Studio is in a UI-only recovery/productization gate. The previous
direction exposed too much engine/debug language and did not behave like a
usable scene editor. The recovery target is a simple visual production editor
for end users.

## Recovery Decision

The Studio main UI is organized around:

```text
Project -> Scenes/Canvas -> Sources -> Layers -> Outputs/Sinks
```

Engine/backend/render internals are hidden from the primary UI. Advanced
diagnostics can be added later behind an explicit advanced surface.

## Current Gate

Allowed:

- native Avalonia/MVVM mock shell;
- shared `StudioDocument`;
- project explorer;
- scene-scoped layer editing;
- mock canvas editor with zoom/pan/select/move/resize;
- contextual `Propriedades`;
- bottom `Camadas`, `Efeitos`, `Saidas`;
- mock dialogs and fake command state;
- pt-BR localization foundation;
- ViewModel tests.

Blocked:

- real webcam/desktop/media adapters;
- real RTMP/SRT/NDI/virtual-camera/encoder outputs;
- real audio;
- productive GPU preview wiring before runtime preview reliability is approved;
- legacy direct preview/capture paths.

## Acceptance

The recovery gate is acceptable when:

- the app behaves like a visual editor, not a debug prototype;
- selecting scenes changes layer/canvas context;
- selecting/moving/resizing mock layers works;
- source-to-current-scene and output-to-scene routing are clear;
- `Propriedades` edits shared mock objects;
- effects are contextual to the selected layer;
- the bottom workbench has only `Camadas`, `Efeitos`, `Saidas`;
- main UI has no engine/debug controls;
- tests cover the above behavior.
