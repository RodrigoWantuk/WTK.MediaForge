# WTK MediaForge Studio UI Design

This document defines the first product UI direction for **WTK MediaForge Studio**, the desktop application layer for users who want a complete tool instead of direct API usage.

The Studio UI is a separate product shell on top of the MediaForge engine. It must make the pipeline approachable without exposing internal GPU lifetime details, native handles, render-thread ownership, sink queues, or backend-specific resource management.

## Design Decision

Adopt the second designer mockup as the primary visual reference for the first Studio shell.

Reference assets:

- `docs/assets/ui/studio-shell-reference.png`
- `docs/assets/ui/studio-shell-polished-reference.png`
- `docs/assets/ui/bottom-layers-reference.png`
- `docs/assets/ui/bottom-effects-reference.png`
- `docs/assets/ui/bottom-diagnostics-reference.png`
- `docs/assets/ui/inspector-output-reference.png`

The earlier Canva concept remains a secondary reference for spacing and simplicity, but the second mockup is the product direction because it better communicates a real live-production tool, richer pipeline status, contextual inspectors, output health, diagnostics, and future audio placement.

## Product Positioning

WTK MediaForge Studio is a professional dark-theme desktop application for:

- scene composition;
- source/input management;
- layer ordering and transforms;
- effect chains;
- preview/canvas inspection;
- output routing and monitoring;
- diagnostics/performance visibility;
- future audio mixing and mux support.

It should feel conceptually familiar to users of live-production tools such as OBS/vMix/Wirecast, but it must not copy their UI. The identity should be technical, compact, GPU-aware, and branded around MediaForge.

## Technology Target

- Avalonia UI.
- .NET 8 or later according to the solution target.
- C#.
- MVVM.
- CommunityToolkit.Mvvm.
- Compiled bindings enabled.
- No WebView.
- No Electron.
- No React runtime.
- No direct dependency on the Lovable prototype.
- Multiplatform target: Windows, Linux, macOS.

The React/Lovable project is a **visual and component-behavior reference only**. It must be translated into Avalonia styles, controls, view models, templates, and commands.

## Theme Direction

Initial scope: dark theme only.

Recommended semantic tokens for Avalonia resources:

| Token | Suggested color | Usage |
|---|---:|---|
| `MfBackgroundBrush` | `#10141B` | app background |
| `MfSurface1Brush` | `#151A22` | primary panels |
| `MfSurface2Brush` | `#1A2029` | title/toolbar/panel headers |
| `MfSurface3Brush` | `#222A35` | hover/field/card surfaces |
| `MfBorderBrush` | `#2A3340` | normal panel borders |
| `MfBorderStrongBrush` | `#3A4654` | input borders and selected edges |
| `MfTextPrimaryBrush` | `#EEF3FA` | primary text |
| `MfTextSecondaryBrush` | `#A8B4C4` | secondary text |
| `MfTextMutedBrush` | `#758195` | muted metadata |
| `MfAccentBrush` | `#00AEEF` | selection, active tabs, primary actions |
| `MfAccentSoftBrush` | `#123B56` | selected rows / soft accents |
| `MfSuccessBrush` | `#36D68A` | running/healthy states |
| `MfWarningBrush` | `#F0A83A` | warnings / buffering |
| `MfErrorBrush` | `#F05252` | stopped/error/recording emphasis |
| `MfRecordingBrush` | `#F05252` | record/live states |

Typography:

- UI text: Inter if available, otherwise `Segoe UI`, `Roboto`, `Arial`, sans-serif fallback.
- Numeric/diagnostic text: JetBrains Mono if available, otherwise `Consolas`, `Cascadia Mono`, monospace fallback.
- Do not commit font files unless their licenses and distribution terms are explicitly reviewed.

General visual rules:

- compact desktop density;
- no mobile-style oversized cards;
- panels separated by thin borders;
- active tabs indicated with accent bottom border;
- selected rows use soft accent background and optional accent left border;
- warning/error/success states must be visible but not garish;
- all numeric telemetry should use tabular/monospace rendering where practical.

## Shell Layout

The first Studio shell uses a fixed desktop workbench layout with resizable panels later.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Title/App bar: logo, project name, menus, engine/FPS/drop/GPU/window status  │
├──────────────────────────────────────────────────────────────────────────────┤
│ Toolbar: New/Open/Save, Add Source, Add Scene, Start/Stop, Stream/Record     │
├───────────────┬───────────────────────────────────────────────┬──────────────┤
│ Project       │ Preview header / canvas controls              │ Inspector    │
│ Explorer      ├───────────────────────────────────────────────┤              │
│               │ Canvas / Preview viewport                     │              │
│               ├───────────────────────────────────────────────┤              │
│               │ Bottom Workbench tabs                         │              │
│               │ Layers / Effects / Timeline / Diagnostics     │              │
│               │ Performance / Output Monitor / Audio Mixer    │              │
├───────────────┴───────────────────────────────────────────────┴──────────────┤
│ Status bar: running state, backend, FPS, frame time, dropped, outputs, warn  │
└──────────────────────────────────────────────────────────────────────────────┘
```

Recommended initial dimensions:

- title bar: 36 px;
- toolbar: 44 px;
- left project explorer: 256 px;
- right inspector: 320 px;
- bottom workbench: 240 px;
- status bar: 27 px;
- preview canvas consumes remaining space;
- minimum app window: approximately 1280 x 720;
- comfortable target: 1600 x 900 or larger.

Avalonia layout skeleton:

```xml
<Grid RowDefinitions="36,44,*,27">
  <views:StudioTitleBarView Grid.Row="0" />
  <views:StudioToolbarView Grid.Row="1" />

  <Grid Grid.Row="2" ColumnDefinitions="256,*,320">
    <views:ProjectExplorerView Grid.Column="0" />

    <Grid Grid.Column="1" RowDefinitions="36,*,240">
      <views:PreviewHeaderView Grid.Row="0" />
      <views:PreviewCanvasView Grid.Row="1" />
      <views:BottomWorkbenchView Grid.Row="2" />
    </Grid>

    <views:InspectorView Grid.Column="2" />
  </Grid>

  <views:StudioStatusBarView Grid.Row="3" />
</Grid>
```

The final version may add `GridSplitter` columns/rows and user-persisted layout sizes.

## Primary Regions

### 1. Title Bar

Purpose:

- branding;
- project identity;
- top-level menus;
- compact technical health summary.

Required content:

- WTK MediaForge Studio logo/name;
- project name and dirty marker;
- menus: File, Edit, View, Scene, Source, Output, Tools, Help;
- engine state indicator;
- render FPS;
- dropped frames;
- GPU usage/health;
- native window controls where using a custom chrome.

Initial state may be fake/mock. Do not connect GPU metrics until diagnostics exist in the engine.

### 2. Toolbar

Purpose:

- common project and runtime actions.

Required buttons:

- New;
- Open;
- Save;
- Add Source;
- Add Scene;
- Start/Stop Engine;
- Start Streaming;
- Start Recording;
- Settings.

Implementation:

- each action must be a command in the shell/view model;
- enabled/disabled state comes from command `CanExecute` or bound state, not code-behind manipulation;
- stream/record buttons must support idle, configured, live/recording, error, and disabled visual states.

### 3. Project Explorer

Purpose:

- one compact navigation tree for all project-level entities.

Required groups:

- Scenes;
- Sources;
- Outputs;
- Presets;
- Packages.

Required row behaviors:

- active/selected state;
- icon by entity kind;
- optional metadata text on the right;
- optional health dot or badge;
- context menu later;
- add/import/delete actions in footer;
- search affordance in header.

Examples:

- `Main Scene` with green active dot;
- `Webcam` with `BRIO` metadata;
- `Intro.mp4` with `BUFFER` badge;
- `Recording MP4` with red recording dot;
- `RTMP · Twitch` with bitrate metadata and live pulse;
- `Brand Kit` with `v2` badge.

### 4. Preview / Canvas

Purpose:

- central visual composition area.

Required concepts:

- checkerboard background around the canvas;
- preview header with current scene and canvas metadata;
- zoom controls: zoom out, zoom percent, zoom in, fit, 100%;
- toggles: grid, safe area;
- 16:9 canvas with shadow/ring;
- selection bounds for selected layer;
- handles for resize;
- future drag/resize/rotate interactions;
- render timing overlay optional.

Initial implementation rule:

- implement a fake/design canvas first;
- do not bind real `PreviewPanelSink` until the preview reliability milestone is active and green;
- real native/GPU preview must be isolated behind a dedicated preview host/control, not scattered through the shell.

### 5. Inspector

Purpose:

- contextual property editing for the selected entity.

Required target types:

- Scene;
- Source;
- Layer;
- Effect;
- Output;
- Package/Preset later.

Required sections for layer selection:

- header: selected item name and kind;
- Transform: X/Y, W/H, rotation slider, opacity slider, blend mode;
- Crop: left, top, right, bottom;
- Effects: list of attached effects, enabled state, visibility/settings actions.

Required sections for source selection:

- Device/Type configuration;
- selected device/path/url;
- resolution/frame rate if applicable;
- status and reconnect action.

Required sections for output selection:

- Destination;
- stream/server/path settings;
- stream key masked by default;
- encoder settings;
- health status.

Implementation:

- use contextual sub-view models or view locator/data templates;
- the inspector must never know engine internals directly;
- secret fields must be redacted/masked by default.

### 6. Bottom Workbench

Purpose:

- high-density project editing and diagnostics area.

Required tabs:

- Layers;
- Effects;
- Timeline;
- Diagnostics;
- Performance;
- Output Monitor;
- Audio Mixer (`BETA` / future placeholder).

The tabs can share a common `BottomWorkbenchViewModel` and expose a selected tab enum/string.

#### Layers tab

Table-like list with:

- drag handle;
- visibility;
- lock;
- order number;
- type icon/thumbnail;
- layer name;
- type;
- source;
- selected row state.

#### Effects tab

Effect chain cards with:

- drag handle;
- enable toggle;
- effect name;
- reset/settings actions;
- expanded parameter editor for selected/enabled effects;
- example: Chroma Key with key color, similarity, smoothness, spill.

#### Diagnostics tab

Log rows with:

- time;
- level;
- category;
- message;
- color-coded level;
- monospace rendering.

#### Performance tab

Metric cards:

- render FPS;
- frame time;
- dropped frames;
- GPU/VRAM;
- sparkline/frame-time chart.

#### Output Monitor tab

Output table with:

- name;
- type;
- bitrate;
- FPS;
- status;
- action button.

Required output states:

- Running;
- Recording;
- Live;
- Planned;
- Error;
- Disabled.

#### Audio Mixer tab

Future placeholder only until audio track starts.

The placeholder may show mock strips for layout reservation, but must be clearly tagged as `BETA` or future. Do not implement real audio capture/mix/mux before the roadmap allows it.

### 7. Status Bar

Purpose:

- always-visible compact runtime summary.

Required items:

- engine state;
- backend;
- FPS;
- frame time;
- dropped frames;
- active scene;
- active outputs;
- warnings count;
- app version.

The status bar should duplicate only the most useful technical indicators. Avoid redundant project status if already shown in the title bar.

## Interaction Model

Initial mock implementation must support:

- selecting a scene/source/output/layer from fake data;
- active selection updates the inspector;
- bottom tabs switch content;
- start/stop engine toggles fake running state and button state;
- layer rows show selected state;
- effects tab can show an expanded Chroma Key card;
- output monitor can show running/live/recording/planned examples.

Future interactions:

- drag source into canvas;
- drag layer reorder;
- drag/resize layer on canvas;
- grid/safe-area overlays;
- context menus;
- add source wizard;
- add output wizard;
- property validation;
- project serialization;
- real engine diagnostics.

## Required Reusable Controls

Create reusable Avalonia controls/styles instead of duplicating markup.

Suggested controls:

- `StudioTitleBarView`;
- `StudioToolbarView`;
- `ToolButton` style/control;
- `StatusIndicatorView`;
- `Badge` style/control;
- `ProjectExplorerView`;
- `ProjectTreeGroupView`;
- `ProjectTreeItemView`;
- `PreviewCanvasView`;
- `PreviewToolbarView`;
- `SelectionAdornerView`;
- `InspectorView`;
- `InspectorSectionView`;
- `UnitNumberBox` or styled numeric field;
- `ValueSliderView`;
- `LayerTableView`;
- `LayerTableRowView`;
- `EffectsChainView`;
- `EffectCardView`;
- `DiagnosticsLogView`;
- `PerformanceView`;
- `MetricCardView`;
- `SparklineView`;
- `OutputMonitorView`;
- `AudioMixerPlaceholderView`;
- `StudioStatusBarView`.

## Required View Models

Suggested first-pass view model structure:

```text
StudioShellViewModel
  TitleBarViewModel
  ToolbarViewModel
  ProjectExplorerViewModel
  PreviewCanvasViewModel
  InspectorHostViewModel
  BottomWorkbenchViewModel
  StatusBarViewModel
```

Supporting models/view models:

```text
StudioSelectionViewModel
ProjectTreeGroupViewModel
ProjectTreeItemViewModel
SceneItemViewModel
SourceItemViewModel
OutputItemViewModel
PresetItemViewModel
PackageItemViewModel
LayerItemViewModel
EffectItemViewModel
DiagnosticLogItemViewModel
PerformanceMetricViewModel
OutputMonitorItemViewModel
AudioStripViewModel
```

Selection should be a first-class concept:

```csharp
public enum StudioSelectionKind
{
    None,
    Scene,
    Source,
    Layer,
    Effect,
    Output,
    Preset,
    Package
}
```

Inspector pages should be selected from `StudioSelectionKind` and selected item data.

## Command Model

At minimum:

- `NewProjectCommand`;
- `OpenProjectCommand`;
- `SaveProjectCommand`;
- `AddSourceCommand`;
- `AddSceneCommand`;
- `ToggleEngineCommand` or separate `StartEngineCommand` / `StopEngineCommand`;
- `StartStreamingCommand`;
- `StartRecordingCommand`;
- `OpenSettingsCommand`;
- `SelectProjectItemCommand`;
- `SelectLayerCommand`;
- `ToggleLayerVisibilityCommand`;
- `ToggleLayerLockCommand`;
- `AddEffectCommand`;
- `ToggleEffectEnabledCommand`;
- `SelectBottomTabCommand`;
- `ReconnectSourceCommand`.

Use `RelayCommand` / `AsyncRelayCommand` from CommunityToolkit.Mvvm. Button enablement must come from command `CanExecute` or bound state.

## Implementation Rules

- Do not manipulate controls directly from view models.
- Do not put product logic in `.axaml.cs`.
- Use code-behind only for purely visual behavior or platform/native control hosting.
- Use compiled bindings and `x:DataType` in views where practical.
- Use `ObservableCollection<T>` for lists that mutate.
- Prefer `DataTemplate` and view locator for contextual inspector pages.
- Keep fake UI data isolated behind a design/mock state provider.
- The first UI milestone must run without any GPU, Vulkan, D3D11, capture, encoder, or live source dependency.
- The Studio UI must not revive legacy preview/capture paths.

## First Milestone Acceptance

The first UI milestone is accepted when:

- `WTK.MediaForge.Studio` builds and runs;
- dark theme resources are centralized;
- shell layout resembles the approved reference;
- all major regions are visible;
- fake project data populates Project Explorer, canvas, inspector, bottom tabs, and status bar;
- selection changes update the inspector;
- Start/Stop Engine toggles fake state and visual indicators;
- bottom tabs switch between Layers, Effects, Timeline placeholder, Diagnostics, Performance, Output Monitor, and Audio Mixer placeholder;
- no engine/runtime integration is required;
- no WebView/React/Electron dependency exists;
- no new runtime adapters or sinks are introduced for the UI milestone.

