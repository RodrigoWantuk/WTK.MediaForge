# Studio UI Visual Acceptance

Use this checklist before considering a Studio UI reset change complete.

- Automated viewport contract:
  `./scripts/verify-studio-ui-visual-qa.ps1` must pass and generate
  `test-reports/studio-visual-qa-report.md`.
- App looks like a real desktop media editor, not a debug tool.
- Left panel shows scene cards only, with search, selected state, main-scene
  badge, resolution/FPS, and linked outputs.
- Source library is opened from `+ Fonte`; sources are not mixed into the scene
  list.
- Preview/canvas supports selecting, moving, resizing, grid, safe area, fit, and
  100% zoom controls with mock layers.
- Zoom never pushes the canvas into a corner; mouse zoom preserves the point
  under the cursor.
- Preview frame host is separated from editable overlays so a real frame source
  can be wired later without rewriting interaction logic.
- `Produção / Saídas` cards are always visible and show routed scene,
  transition, state, and send-scene action.
- Output scene changes happen through an explicit send workflow, not a loose
  combo box.
- `Propriedades` uses editable controls for editable values: `NumericUpDown`,
  `Slider`, `ComboBox`, toggles, status cards, and masked secrets.
- Scene effects live in scene properties.
- Layer effects live in layer properties.
- Output transitions live in output routing/properties.
- Bottom workbench has only `Camadas` and `Saídas da cena`.
- Diagnostics, performance, timeline, audio mixer, and global effects are not
  main tabs.
- Status bar is compact and does not expose engine/backend/native handles,
  leases, fences, command buffers, keyed mutexes, or backend-owned surfaces.
- Primary interactive controls expose stable automation ids, accessible names,
  and help text where the action is not obvious.
- `MainWindow` can be loaded by the Avalonia Headless smoke test without
  resource, binding, or XAML failures.
- 1366x768 remains usable.
- 1920x1080 feels comfortable.
- Dark theme uses semantic tokens instead of scattered hard-coded colors.
- Main shell strings use the localization infrastructure.
- Runtime/media/audio integrations remain blocked until the roadmap gate opens.

Manual review is still required for visual taste, color balance, perceived
polish, and interaction feel. The automated QA gate covers regression-prone
structure, viewport usability, centered fit zoom, and primary UI hygiene.
