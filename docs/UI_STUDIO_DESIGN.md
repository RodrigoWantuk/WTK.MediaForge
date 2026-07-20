# WTK MediaForge Studio UI Design

This document defines the current Studio v13 product direction. Studio is a
visual production editor for end users, not a debug console for the MediaForge
engine.

## Product Model

The visible model is:

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

Rules:

- a source is reusable project media/input definition;
- a layer is one source instance inside one scene;
- layers belong to the current scene only;
- layer effects live in layer properties;
- scene effects live in scene properties;
- output transitions belong to the workflow of sending a scene to an output;
- outputs/sinks never force a second render from the UI;
- engine/backend/GPU/fence/lease/registry/render-thread concepts are hidden
  from the main UI.

## Main Shell

The main screen is a scene editor:

- top bar: logo/product, project name, save state, simple product status;
- action bar: `Novo`, `Abrir`, `Salvar`, `+ Cena`, `+ Fonte`,
  `Configurar saídas`, `Transmitir`, `Gravar`;
- left: `Cenas` only, with searchable scene cards;
- center: dominant visual scene editor with canvas, zoom, pan, selection,
  drag, resize handles, grid, and safe area;
- right top: always-visible `Produção / Saídas` cards;
- right bottom: contextual `Propriedades`;
- bottom: `Camadas` and `Saídas da cena` only;
- status bar: product/project status, current scene, configured outputs, and
  dropped frame count.

There is no classic top menu bar in the v0.2 workspace. Context actions stay
near their objects: add scene in the scene panel, add source through the source
library, route scenes through the production/output cards.

## Canvas Editor

`StudioCanvasEditor` owns the Avalonia editing overlay. Until the preview gate
passes it uses a mock visual beneath that overlay; later the same overlay sits
above the native GPU surface.

Required behavior:

- `SceneViewportState` owns all viewport math;
- `screen = scene * zoom + offset`;
- `Fit` centers the scene inside the available viewport;
- mouse-wheel zoom preserves the scene point under the cursor;
- button zoom uses the viewport center;
- `100%` equals scale `1.0`;
- click selects the topmost visible layer;
- click on empty space clears layer selection and shows scene properties;
- drag moves unlocked layers;
- real clickable resize handles resize unlocked layers;
- `Shift` constrains move axis or keeps resize aspect;
- `Alt` resizes from center;
- `Esc` clears selection;
- `Ctrl+0` fits and `Ctrl+1` returns to 100%;
- middle drag or Space+drag pans;
- arrow keys nudge the selected layer.

The future real preview frame must sit below the Avalonia overlay. Selection,
handles, grid, and safe-area remain Avalonia overlay behavior.

## Propriedades

The right panel is an editing surface, not a label report.

Context pages:

- Scene: name, canvas size, FPS, background, program flag, linked outputs,
  scene-level effects;
- Source: type, origin/device, resolution, FPS, status, add-to-current-scene;
- Layer: position, size, rotation, crop, opacity, blend, visibility, lock,
  layer effects;
- Output: current routed scene as read-only context, explicit send-another-scene
  action, default transition, destination, quality, and masked secrets.

Output scene routing must not be a loose combo box. It is an explicit action
from `Produção / Saídas` or output properties.

## Source Library

The left panel does not list sources. `+ Fonte` opens a source library dialog
with product choices such as Webcam, Tela, Imagem, Vídeo, Texto, Cor sólida,
NDI planned, and RTSP/IP planned. In v0.2, available items create mock sources
and add a layer to the current scene.

## Runtime Boundary

Studio v0.2 remains UI/mock-only. It must not add or wire real webcam, desktop
capture, media decode, RTMP/SRT, NDI, virtual camera, encoder, audio, or
productive GPU preview integration. Runtime integration remains gated by
`docs/ROADMAP_CURRENT.md`.
