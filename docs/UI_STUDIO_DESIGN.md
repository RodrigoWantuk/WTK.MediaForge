# WTK MediaForge Studio UI Design

This document defines the current Studio v0.1 product direction. Studio is a
visual editor for end users, not a debug console for the MediaForge engine.

## Product Model

The visible model is:

```text
Project -> Scenes/Canvas -> Sources -> Layers -> Outputs/Sinks
```

Rules:

- a source is reusable project media/input definition;
- a layer is a source instance inside one scene;
- effects belong to the selected layer;
- an output routes one scene to one destination;
- sinks/outputs never force a second render from the UI;
- internal engine, backend, GPU, fence, lease, registry, and render-thread
  concepts are not shown in the main UI.

## Main Shell

The main screen is a production editor:

- left: project explorer with `Cenas`, `Fontes`, `Saidas`, `Presets`,
  `Pacotes`;
- center: dominant editable canvas for the current scene;
- right: `Propriedades` panel for the selected scene/source/layer/output;
- bottom: only `Camadas`, `Efeitos`, and `Saidas`;
- status bar: project-level status, current scene, configured outputs, dropped
  frame count.

The toolbar contains:

- Novo;
- Abrir;
- Salvar;
- Adicionar fonte;
- Adicionar cena;
- Configurar saida;
- Transmitir;
- Gravar;
- Configuracoes.

There is no `Start Engine` control in the primary UI. The eventual runtime
starts/stops internally as required by product actions.

## Canvas Editor

`StudioCanvasEditor` is the v0.1 mock editor. It is not a real GPU preview.

Required behavior:

- explicit zoom and pan, no `Viewbox`;
- fit zoom is calculated from viewport size;
- `100%` equals scale `1.0`;
- click selects the topmost layer under the pointer;
- drag moves unlocked layers;
- resize handles resize unlocked layers;
- `Shift` constrains axis/aspect behavior;
- `Ctrl` resizes from center;
- middle drag or Space+drag pans;
- arrow keys nudge the selected layer;
- locked layers cannot move or resize;
- checkerboard, grid, and safe-area overlays remain Avalonia overlays.

The future real preview host must sit below the Avalonia overlay instead of
replacing editor behavior.

## Propriedades Panel

The right panel is an editing surface, not a label report. Default width is
about 380 px, with usable min/max bounds.

Context pages:

- Scene: canvas size, FPS, background, layer count, linked outputs;
- Source: type, origin/device, resolution, FPS, status, add-to-current-scene;
- Layer: position, size, rotation, crop, opacity, blend, visibility, lock,
  effects;
- Output: routed scene, enabled/configured state, destination, codec/bitrate,
  masked secrets.

Low-level diagnostics belong in a future advanced view, not the main panel.

## Bottom Workbench

Only three tabs are visible in v0.1:

- `Camadas`: scene-scoped layer list with visibility, lock, order, source, and
  selected state;
- `Efeitos`: effects for the selected layer only;
- `Saidas`: output routes, assigned scene, state, destination, and edit action.

Timeline, audio mixer, performance charts, and diagnostics are future/advanced
surfaces and must not be primary tabs in v0.1.

## Localization And Legibility

The initial build is pt-BR by default. Visible shell strings must come from
resources or product ViewModels and must not mix English UI labels into the main
screen.

Typography:

- normal UI text: at least 13 px;
- secondary metadata: at least 12 px;
- short badges: 11-12 px only;
- no functional 9-10 px text in the main UI.

## Runtime Boundary

Studio v0.1 is mock-only. It must not add or wire real webcam, desktop capture,
media decode, RTMP/SRT, NDI, virtual camera, encoder, audio, or productive GPU
preview integration. Runtime integration remains gated by `docs/ROADMAP_CURRENT.md`.
