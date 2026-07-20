# Studio UI Product Reset Plan

WTK MediaForge Studio is in a UI-only product reset gate. The previous direction
mixed too many concepts in the primary workspace and looked like an engine/debug
prototype. The reset target is a simple visual production editor for end users.

## Reset Decision

The Studio main UI is organized around:

```text
Project
  Scenes
    Layers
      Layer effects
    Scene effects
  Reusable sources
  Outputs
    Routed scene
    Switch transition
    Output state
```

Engine/backend/render internals are hidden from the primary UI. Advanced
diagnostics can be added later behind an explicit advanced surface.

## Current Gate

Allowed:

- native Avalonia/MVVM mock shell;
- shared `StudioDocument`;
- left scene list only;
- source library dialog;
- scene-scoped layer editing;
- mock canvas editor with deterministic zoom/pan/select/move/resize;
- right-side `Produção / Saídas` output cards;
- explicit scene-to-output routing with transition;
- contextual `Propriedades`;
- bottom `Camadas` and `Saídas da cena`;
- pt-BR localization foundation;
- ViewModel and viewport tests.

Blocked:

- real webcam/desktop/media adapters;
- real RTMP/SRT/NDI/virtual-camera/encoder outputs;
- real audio;
- productive GPU preview wiring before runtime preview reliability is approved;
- legacy direct preview/capture paths.

## Acceptance

The reset gate is acceptable when:

- the app behaves like a visual editor, not a debug prototype;
- selecting scenes changes layer/canvas context;
- selecting/moving/resizing mock layers works;
- source-to-current-scene and output-to-scene routing are clear;
- `Propriedades` edits shared mock objects;
- effects are contextual to layer or scene;
- transitions are configured in output routing;
- the bottom workbench has only `Camadas` and `Saídas da cena`;
- main UI has no engine/debug controls and no classic menu bar;
- tests cover the above behavior.
