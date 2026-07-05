# Studio UI Acceptance Checklist

Use this checklist for the Studio v0.1 Avalonia mock editor.

## Scope Control

- [ ] Avalonia UI only.
- [ ] No React, WebView, Electron, Tailwind runtime, or browser dependency.
- [ ] No real source adapter.
- [ ] No real recording/streaming/NDI/virtual-camera sink.
- [ ] No real audio pipeline.
- [ ] No real GPU preview integration.
- [ ] No legacy preview/capture path.

## Product Flow

- [ ] Project Explorer shows `Cenas`, `Fontes`, `Saidas`, `Presets`, `Pacotes`.
- [ ] Selecting a scene changes the current canvas and layer list.
- [ ] Sources are reusable and can be added to the current scene as layers.
- [ ] Layers are scoped to the current scene.
- [ ] Selecting a layer updates canvas selection, `Camadas`, `Efeitos`, and
  `Propriedades`.
- [ ] Selecting a source/scene/output clears layer effect context.
- [ ] Outputs route to scenes and show the assigned scene.
- [ ] `Transmitir` and `Gravar` depend on configured outputs, not engine state.

## Canvas Editor

- [ ] Canvas editor uses explicit scale/translate, not `Viewbox`.
- [ ] Fit zoom is calculated from viewport size.
- [ ] 100% zoom equals scale `1.0`.
- [ ] Click selects the topmost visible layer.
- [ ] Drag moves unlocked layers.
- [ ] Resize handles resize unlocked layers.
- [ ] Locked layers cannot move or resize.
- [ ] Keyboard nudge works for the selected layer.
- [ ] Middle drag or Space+drag pans.
- [ ] Grid and safe-area overlays remain Avalonia overlays.

## Propriedades

- [ ] Panel title is `Propriedades`.
- [ ] Layer page has typed controls for position, size, rotation, crop,
  opacity, blend, lock, visibility, and effects.
- [ ] Source page has origin/device, resolution/FPS, status, and add-to-scene.
- [ ] Scene page has canvas metadata and output routes.
- [ ] Output page has assigned scene, enabled/configured state, destination,
  codec/bitrate, and masked secrets.
- [ ] Secrets are masked by default.

## Main UI Hygiene

- [ ] No `Start Engine`, `Stop Engine`, `GPU idle`, or `Preview idle` appears
  in the primary UI.
- [ ] No Timeline, Audio Mixer, Diagnostics, or Performance tab appears in the
  main bottom workbench.
- [ ] Main visible text is pt-BR for the initial build.
- [ ] Normal text is at least 13 px; metadata is at least 12 px.
- [ ] The preview editor remains the dominant center surface.

## Validation

- [ ] `dotnet build` passes.
- [ ] `dotnet test` passes.
- [ ] `./scripts/test.ps1 -Tier Fast` passes.
- [ ] GPU tier is only required if runtime/render/GPU code changed.
