# Studio UI Acceptance Checklist

Use this checklist for the Studio v0.2 Avalonia mock editor.

## Scope Control

- [ ] Avalonia UI only.
- [ ] No React, WebView, Electron, Tailwind runtime, or browser dependency.
- [ ] No real source adapter.
- [ ] No real recording/streaming/NDI/virtual-camera sink.
- [ ] No real audio pipeline.
- [ ] No real GPU preview integration.
- [ ] No legacy preview/capture path.

## Product Flow

- [ ] The left panel shows `Cenas` only.
- [ ] Selecting a scene changes the current canvas and layer list.
- [ ] Selecting a scene clears the selected layer and shows scene properties.
- [ ] Sources are reusable but added through `+ Fonte` / source library.
- [ ] Layers are scoped to the current scene.
- [ ] Selecting a layer updates canvas selection, `Camadas`, and
  `Propriedades`.
- [ ] Layer effects appear in layer properties.
- [ ] Scene effects appear in scene properties.
- [ ] Outputs show the assigned scene in `Produção / Saídas`.
- [ ] Scene-to-output routing is an explicit send workflow with transition.
- [ ] `Transmitir` and `Gravar` depend on configured outputs, not engine state.

## Canvas Editor

- [ ] Canvas editor uses `SceneViewportState`, not scattered pan/zoom math.
- [ ] Fit centers the scene.
- [ ] Mouse-wheel zoom preserves the point under the cursor.
- [ ] Button zoom uses the viewport center.
- [ ] 100% zoom equals scale `1.0`.
- [ ] Click selects the topmost visible layer.
- [ ] Click empty space clears layer selection.
- [ ] Drag moves unlocked layers.
- [ ] Real resize handles resize unlocked layers.
- [ ] Locked layers cannot move or resize.
- [ ] Keyboard nudge works for the selected layer.
- [ ] Middle drag or Space+drag pans.
- [ ] Grid and safe-area overlays remain Avalonia overlays.

## Propriedades

- [ ] Panel title is `Propriedades`.
- [ ] Scene page has editable name, canvas size, FPS, background, linked
  outputs, and scene effects.
- [ ] Layer page has typed controls for position, size, rotation, crop,
  opacity, blend, lock, visibility, and layer effects.
- [ ] Source page has origin/device, resolution/FPS, status, and add-to-scene.
- [ ] Output page has current routed scene, explicit send-another-scene action,
  default transition, destination, quality, and masked secrets.
- [ ] Output scene routing is not a loose combo box.
- [ ] Secrets are masked by default.

## Main UI Hygiene

- [ ] No classic top menu bar appears in the primary workspace.
- [ ] No `Start Engine`, `Stop Engine`, `GPU idle`, or `Preview idle` appears
  in the primary UI.
- [ ] No Timeline, Audio Mixer, Diagnostics, Performance, or global Effects tab
  appears in the main bottom workbench.
- [ ] Main visible text is pt-BR for the initial build.
- [ ] Required accents render correctly: `Configurações`, `Saídas`,
  `Transmissão`, `Gravação`, `Diagnósticos`, `Prévia`, `Área segura`.
- [ ] Normal text is at least 13 px; metadata is at least 12 px.
- [ ] The preview editor remains the dominant center surface.

## Validation

- [ ] `git diff --stat` reviewed.
- [ ] `dotnet build` passes.
- [ ] `dotnet test` passes.
- [ ] `./scripts/test.ps1 -Tier Fast` passes.
- [ ] GPU tier is only required if runtime/render/GPU code changed.
