# React/Lovable Prototype to Avalonia Mapping

This document maps the downloaded Lovable/React prototype into Avalonia concepts. The prototype is a reference only. Do not embed React, Tailwind, Vite, web assets, or WebView into the Studio app.

## Source Prototype Summary

The downloaded prototype contains the primary UI in:

```text
src/routes/index.tsx
src/styles.css
```

Important React components found in `index.tsx`:

- `StudioMock`
- `TitleBar`
- `Indicator`
- `WindowBtn`
- `ToolBar`
- `ToolBtn`
- `LeftPanel`
- `PanelHeader`
- `TreeGroup`
- `TreeItem`
- `Dot`
- `Chip`
- `CanvasArea`
- `IconBtn`
- `MiniBtn`
- `BottomPanel`
- `LayersView`
- `EffectsView`
- `EffectCard`
- `DiagnosticsView`
- `PerformanceView`
- `Metric`
- `Sparkline`
- `OutputMonitorView`
- `AudioMixerView`
- `EmptyState`
- `RightInspector`
- `InspectorSection`
- `PropRow`
- `NumField`
- `Slider`
- `Select`
- `LayerInspector`
- `SourceInspector`
- `SceneInspector`
- `OutputInspector`
- `StatusBar`

Important style ideas found in `styles.css`:

- semantic color tokens;
- dark surfaces;
- monospace utility;
- checkerboard background for transparent canvas space;
- pulse-dot animation;
- compact desktop typography.

## Translation Principles

| React/Tailwind pattern | Avalonia equivalent |
|---|---|
| component function | `UserControl` + view model, or reusable `Style` |
| `useState` | `ObservableProperty` on view model |
| `className` state classes | Avalonia `Classes`, pseudo-classes, styles, bound properties |
| Tailwind tokens | `App.axaml` resources and brushes |
| mapped arrays | `ItemsControl`, `ListBox`, `TreeView`, `DataGrid`, `DataTemplate` |
| conditional rendering | `ContentControl` + `DataTemplate`, `IsVisible`, or selected tab content |
| button callbacks | bound `ICommand` |
| CSS grid/flex | Avalonia `Grid`, `DockPanel`, `StackPanel` |
| SVG sparkline | custom `Control`, `Path`, or lightweight drawing control |
| `lucide-react` icons | vector path/icon resource layer, do not require React |

## Component Mapping

| Lovable component | Avalonia target | Notes |
|---|---|---|
| `StudioMock` | `StudioShellView` + `StudioShellViewModel` | Root shell only. No engine work in view. |
| `TitleBar` | `StudioTitleBarView` | Custom chrome optional. May use normal window title bar first. |
| `Indicator` | `StatusIndicatorView` / `StatusIndicatorStyle` | Reused in title bar and status bar. |
| `WindowBtn` | optional custom window chrome buttons | Skip initially if normal OS window chrome is used. |
| `ToolBar` | `StudioToolbarView` | All buttons bound to commands. |
| `ToolBtn` | `ToolButton` style | Use variants: default, primary, success, danger, icon-only. |
| `LeftPanel` | `ProjectExplorerView` | Backed by grouped tree/list view models. |
| `PanelHeader` | `PanelHeaderView` / style | Reusable for Project Explorer and Inspector. |
| `TreeGroup` | `ProjectTreeGroupViewModel` + `ItemsControl` or `TreeView` | Needs expand/collapse state. |
| `TreeItem` | `ProjectTreeItemTemplate` | Uses icon, label, metadata, badge/dot, selected state. |
| `Dot` | `HealthDot` style/control | Supports success/warning/recording and pulse. |
| `Chip` | `Badge` style/control | Supports muted/info/warning/planned/beta. |
| `CanvasArea` | `PreviewCanvasView` | First fake/design canvas, later native preview host. |
| `IconBtn` | icon button style | Toolbar/canvas utility buttons. |
| `MiniBtn` | compact toggle button style | Fit, 100%, grid, safe area. |
| `BottomPanel` | `BottomWorkbenchView` | Use `TabControl` or custom tab strip + `ContentControl`. |
| `LayersView` | `LayersPanelView` | Table/grid with selection, lock, visibility. |
| `EffectsView` | `EffectsPanelView` | Effect cards with expand/collapse. |
| `EffectCard` | `EffectCardView` | Reorder, enabled toggle, reset, settings, parameter editor. |
| `DiagnosticsView` | `DiagnosticsPanelView` | Virtualized list later if logs grow. |
| `PerformanceView` | `PerformancePanelView` | Metric cards and sparkline. |
| `Metric` | `MetricCardView` | Reusable compact metric card. |
| `Sparkline` | `SparklineControl` | Draw via `PathGeometry`, `Polyline`, or custom render. |
| `OutputMonitorView` | `OutputMonitorPanelView` | Output table with status and actions. |
| `AudioMixerView` | `AudioMixerPlaceholderView` | Placeholder only until audio roadmap. |
| `EmptyState` | `EmptyStateView` | Generic empty/planned content. |
| `RightInspector` | `InspectorView` + `InspectorHostViewModel` | Uses selected target to resolve child inspector page. |
| `InspectorSection` | `InspectorSectionView` / style | Section header and content spacing. |
| `PropRow` | `InspectorPropertyRow` style/control | Label + editor/content. |
| `NumField` | styled `NumericUpDown` or `TextBox` + validation | Prefer numeric controls where feasible. |
| `Slider` | styled `Slider` + numeric value text | Used for rotation/opacity/effect params. |
| `Select` | styled `ComboBox` | Bound to option collections. |
| `LayerInspector` | `LayerInspectorView` + `LayerInspectorViewModel` | Transform/crop/effects. |
| `SourceInspector` | `SourceInspectorView` + source-type-specific VM | Device/path/status/reconnect. |
| `SceneInspector` | `SceneInspectorView` | Canvas metadata and linked outputs. |
| `OutputInspector` | `OutputInspectorView` | Destination/encoder/health; secrets masked. |
| `StatusBar` | `StudioStatusBarView` | Compact runtime/project summary. |

## Suggested Avalonia File Structure

```text
WTK.MediaForge.Studio/
  App.axaml
  Program.cs

  Styles/
    Theme.axaml
    Controls.axaml
    Icons.axaml
    Typography.axaml

  Views/
    MainWindow.axaml
    Shell/
      StudioTitleBarView.axaml
      StudioToolbarView.axaml
      ProjectExplorerView.axaml
      PreviewCanvasView.axaml
      PreviewHeaderView.axaml
      BottomWorkbenchView.axaml
      InspectorView.axaml
      StudioStatusBarView.axaml
    Panels/
      LayersPanelView.axaml
      EffectsPanelView.axaml
      DiagnosticsPanelView.axaml
      PerformancePanelView.axaml
      OutputMonitorPanelView.axaml
      AudioMixerPlaceholderView.axaml
    Inspectors/
      LayerInspectorView.axaml
      SourceInspectorView.axaml
      SceneInspectorView.axaml
      OutputInspectorView.axaml
    Controls/
      Badge.axaml
      HealthDot.axaml
      MetricCard.axaml
      SparklineControl.cs
      SelectionAdornerControl.cs

  ViewModels/
    StudioShellViewModel.cs
    TitleBarViewModel.cs
    ToolbarViewModel.cs
    ProjectExplorerViewModel.cs
    PreviewCanvasViewModel.cs
    BottomWorkbenchViewModel.cs
    InspectorHostViewModel.cs
    StatusBarViewModel.cs
    Panels/
    Inspectors/

  DesignData/
    StudioDesignData.cs
```

## Theme Resource Translation

Create `Styles/Theme.axaml` with semantic brushes. Do not hard-code colors in every view.

Example resource names:

```xml
<Color x:Key="MfBackgroundColor">#10141B</Color>
<SolidColorBrush x:Key="MfBackgroundBrush" Color="{StaticResource MfBackgroundColor}" />
<SolidColorBrush x:Key="MfSurface1Brush" Color="#151A22" />
<SolidColorBrush x:Key="MfSurface2Brush" Color="#1A2029" />
<SolidColorBrush x:Key="MfSurface3Brush" Color="#222A35" />
<SolidColorBrush x:Key="MfBorderBrush" Color="#2A3340" />
<SolidColorBrush x:Key="MfTextPrimaryBrush" Color="#EEF3FA" />
<SolidColorBrush x:Key="MfTextSecondaryBrush" Color="#A8B4C4" />
<SolidColorBrush x:Key="MfTextMutedBrush" Color="#758195" />
<SolidColorBrush x:Key="MfAccentBrush" Color="#00AEEF" />
<SolidColorBrush x:Key="MfSuccessBrush" Color="#36D68A" />
<SolidColorBrush x:Key="MfWarningBrush" Color="#F0A83A" />
<SolidColorBrush x:Key="MfErrorBrush" Color="#F05252" />
```

## Prototype Data Mapping

Initial fake state should reproduce the reference without engine integration.

Recommended design data:

```text
Scenes
  Main Scene [active]
  Interview
  Break BRB

Sources
  Webcam [BRIO]
  Desktop Capture [Disp 1]
  Logo.png [Image]
  Lower Third [Text]
  Intro.mp4 [Media, BUFFER]

Outputs
  Preview [Local, running]
  Recording MP4 [H.264, recording]
  RTMP · Twitch [6 Mb/s, live]

Presets
  1080p Streaming
  YouTube 1080p60

Packages
  Starter Pack
  Brand Kit [v2]

Layers
  Lower Third [Text, selected]
  Logo.png [Image]
  Webcam [Video]
  Desktop Capture [Video]
```

This design data should live in a mock/design provider, not inside views.

## Implementation Milestones

### UI-1: Static shell

- Build `MainWindow` with shell regions.
- Add dark theme resources.
- Add placeholder controls.
- No behavior beyond app startup.

### UI-2: Mock MVVM state

- Add design data provider.
- Bind Project Explorer, Layers, Diagnostics, Output Monitor, Status Bar.
- Implement bottom tab switching.
- Implement selected item state.

### UI-3: Contextual inspector

- Add inspector host.
- Add layer/source/scene/output inspector pages.
- Bind selection to inspector page.
- Keep values fake but editable in UI state.

### UI-4: Canvas overlay prototype

- Add checker background.
- Add fake 16:9 canvas.
- Add fake selected layer bounds and handles.
- Implement zoom/safe/grid toggles as UI state only.

### UI-5: Command shell

- Add New/Open/Save/AddSource/AddScene/StartStop commands.
- Start/Stop toggles fake state only.
- Stream/Record buttons show configured/live/recording states but do not start real outputs.

### UI-6: Product model integration

- Bind shell state to MediaForge project model where safe.
- Load/save package/project data.
- Keep preview and adapters mocked until runtime milestones allow real integration.

### UI-7: Runtime integration later

- Only after preview reliability gate: bind preview panel through the approved `PreviewPanelSink`/runtime path.
- Do not revive legacy preview/capture code.

## Non-goals

Do not implement in the UI prototype:

- real webcam adapter;
- real desktop capture adapter;
- real RTMP/recording output;
- real audio mixer;
- real NDI/RTSP/HLS;
- real GPU preview integration;
- plugin APIs;
- WebView-based preview;
- embedded React/HTML.

