# Studio UI Visual Acceptance

Use this checklist before considering a Studio UI recovery change complete.

- App looks like a real desktop media editor, not a debug tool.
- Project Explorer has vector icons, clear grouping, badges, search, and selected
  state.
- Preview/canvas supports selecting, moving, resizing, grid, safe area, fit, and
  100% zoom controls with mock layers.
- Preview frame host is separated from editable overlays so a real frame source
  can be wired later without rewriting interaction logic.
- `Propriedades` uses editable controls for editable values: `NumericUpDown`,
  `Slider`, `ComboBox`, toggles, status cards, and masked secrets.
- Layer property edits update the canvas and bottom layer table immediately.
- Source inspector shows product language such as Webcam and Desktop Capture,
  with technical ids hidden under advanced details.
- Output properties keep stream keys masked by default and show the assigned
  scene.
- Bottom Workbench has only useful main workflow tabs: Camadas, Efeitos, Saidas.
- Diagnostics, performance, timeline, and audio mixer are not main tabs in
  v0.1.
- Status bar is compact and does not expose engine/backend/native handles,
  leases, fences, command buffers, keyed mutexes, or backend-owned surfaces.
- 1366x768 remains usable.
- 1920x1080 feels comfortable.
- Dark theme uses semantic tokens instead of scattered hard-coded colors.
- Main shell strings use the localization infrastructure.
- Runtime/media/audio integrations remain blocked until the roadmap gate opens.
