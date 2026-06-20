# WTK MediaForge — Product Model

This document is the **product contract** for WTK MediaForge. It describes what the user composes (sources, canvases, effects, outputs) and how that model must stay separate from GPU execution.

**Rule:** no new media feature (webcam, NDI, RTSP, MP4, preview UI, streaming, recording) may be implemented outside this contract. If something is missing here, update this document first.

For GPU lifecycle, render thread, snapshots, and backend contracts, see [ARCHITECTURE.md](../ARCHITECTURE.md).

---

## Two layers — do not mix them

```text
┌─────────────────────────────────────────────────────────────┐
│  Product Model / Composition Model                          │
│  What the user wants to build                               │
│                                                             │
│  MediaForgeProject                                          │
│    SourceDefinitions, Canvases, Outputs                     │
│    DrawObjects, Effects (planned), typed settings (planned)   │
│    MediaForgeProjectEditor (planned)                        │
│    MediaForgeEngine facade (planned)                        │
└──────────────────────────┬──────────────────────────────────┘
                           │ validate → immutable snapshot
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Runtime / GPU Execution Model                              │
│  How frames are produced, composed, and delivered            │
│                                                             │
│  CompositionRuntime + IVideoFrameProvider                   │
│  ProjectStateSnapshot → RenderFrameSnapshot                 │
│  MediaForgeRenderThread + IRenderBackend                    │
│  PendingRenderSubmissionTracker, GpuFrameLease              │
│  Source providers (live) + output sinks (planned)           │
└─────────────────────────────────────────────────────────────┘
```

| Concern | Product layer | Runtime layer |
|--------|---------------|---------------|
| Editable state | `MediaForgeProject` on UI/coordinator thread | Not editable during render |
| Video inputs | `MediaForgeSourceDefinition` + `SourceLayerDrawObject` | `IVideoFrameProvider`, GPU frames |
| Composition tree | `MediaForgeCanvas` + draw objects | `RenderCanvasSnapshot` |
| Outputs | `MediaForgeRenderOutput` | `RenderOutputBindingSnapshot`, sinks |
| Effects | `MediaForgeEffect` list on draw object (planned) | Shader passes / pipeline stages |
| Validation | `MediaForgeProjectValidator` | Snapshot build diagnostics |

**Anti-patterns (forbidden):**

- `WebcamDrawObject`, `NdiDrawObject`, `RtspDrawObject` — use `SourceLayerDrawObject` + `SourceDefinition`.
- NDI/RTMP logic inside `MediaForgeVulkanRenderer` — use output type + sink factory.
- Chroma key as a property on `SourceLayerDrawObject` — use `MediaForgeEffect`.
- UI code calling `project.Canvases.Add(...)` directly — use `MediaForgeProjectEditor` (planned).
- App wiring `CompositionRuntime`, render thread, and providers manually — use `MediaForgeEngine` (planned).

---

## 1. MediaForgeProject

**Status:** implemented (foundation)

Root serializable document. Do not introduce parallel roots (`SceneModel`, `CompositionDocument`, etc.).

```csharp
public sealed class MediaForgeProject
{
    public int SchemaVersion { get; set; }
    public string CreatedWithVersion { get; set; }
    public string SavedWithVersion { get; set; }

    public List<MediaForgeSourceDefinition> SourceDefinitions { get; set; }
    public List<MediaForgeCanvas> Canvases { get; set; }
    public List<MediaForgeRenderOutput> Outputs { get; set; }
}
```

**Responsibilities:**

- Persist the user's scene: sources, canvases, routing to outputs.
- Serialize/deserialize via `MediaForgeProjectSerializer` + migrator.
- Validate via `MediaForgeProjectValidator` before runtime use.

---

## 2. SourceDefinition

**Status:** implemented (partial — type catalog incomplete)

A **source** is a runtime producer of media (video and eventually audio). It is defined once and referenced many times on canvases.

```csharp
public sealed class MediaForgeSourceDefinition
{
    public SourceId Id { get; set; }
    public string Name { get; set; }
    public MediaSourceTypeId TypeId { get; set; }
    public int SchemaVersion { get; set; }
    public JsonObject Settings { get; set; }   // storage; not the public editing API
}
```

**Product rule:** one `SourceDefinition` can appear in multiple `SourceLayerDrawObject` instances with different crop, transform, effects, and layout.

**Current type ids** (legacy, to be migrated):

| Legacy id | Planned official id |
|-----------|---------------------|
| `wtk.desktop.capture` | `wtk.source.desktop` |
| `wtk.image.file` | `wtk.source.image.file` |
| `wtk.video.file` | `wtk.source.video.file` |

**Planned official catalog** (`MediaSourceTypes` — Commit H2):

| Type id | Display | Live | Video | Audio | GPU interop |
|---------|---------|------|-------|-------|-------------|
| `wtk.source.desktop` | Desktop capture | yes | yes | no | yes |
| `wtk.source.webcam` | Webcam | yes | yes | optional | yes |
| `wtk.source.ndi.input` | NDI input | yes | yes | optional | yes |
| `wtk.source.rtsp.input` | RTSP stream | yes | yes | optional | yes |
| `wtk.source.video.file` | Video file | no | yes | optional | yes |
| `wtk.source.image.file` | Image file | no | yes | no | yes |
| `wtk.source.window.capture` | Window / form capture | yes | yes | no | yes |
| `wtk.source.generated` | Internal/generated | varies | varies | varies | yes |

**Typed settings** (Commit H2): DTOs such as `DesktopCaptureSourceSettings`, `WebcamSourceSettings`, `NdiInputSourceSettings`, etc., serialized through `MediaSourceSettingsSerializer`. Application code must not manipulate raw `JsonObject` outside serializer/validator/migrator.

**Runtime counterpart:** `IVideoFrameProvider` registered in `CompositionRuntime` by `SourceId`.

---

## 3. SourceLayerDrawObject

**Status:** implemented

Visual use of a source on a canvas. All video input types share this draw object.

```csharp
public sealed class SourceLayerDrawObject : MediaForgeDrawObject
{
    public SourceId SourceId { get; set; }
    public LayoutMode LayoutMode { get; set; }
    public DisplayRotation? ContentRotationOverride { get; set; }
}
```

Inherited from `MediaForgeDrawObject`: `Transform`, `Crop`, `Opacity`, `BlendMode`, `Enabled`, and (planned) `Effects`.

**Example:** one webcam source, three layers — fullscreen, PiP crop, magnified region — same `SourceId`, different transforms and effects.

---

## 4. Canvas

**Status:** implemented

The primary composition unit. A canvas holds an ordered list of draw objects and can be nested via `CanvasDrawObject`.

```csharp
public sealed class MediaForgeCanvas
{
    public CanvasId Id { get; set; }
    public string Name { get; set; }
    public FrameSize Size { get; set; }
    public ColorRgba BackgroundColor { get; set; }
    public List<MediaForgeDrawObject> Objects { get; set; }
}
```

**Product rules:**

- Canvas is the unit of visual composition (not Preview/Program — those are UI conventions).
- Nested canvas: `CanvasDrawObject` references `NestedCanvasId`.
- **Cycles are forbidden** (Commit H6).
- **Max nesting depth: 8** (product contract). Enforced by [`CanvasGraphValidator`](../WTK.MediaForge.Composition/Validation/CanvasGraphValidator.cs) and [`RenderFrameSnapshotFactory`](../WTK.MediaForge.Composition/Snapshots/RenderFrameSnapshotFactory.cs) via shared [`CanvasGraphLimits.MaxNestedCanvasDepth`](../WTK.MediaForge.Composition/Validation/CanvasGraphLimits.cs).

**Rendering strategy (target):**

```text
1. Render nested canvas to offscreen Vulkan target
2. Sample that target as a layer in the parent canvas
3. Render parent canvas to bound outputs
```

---

## 5. Draw objects

**Status:** implemented (effects not yet on base type)

| Type | Role | Status |
|------|------|--------|
| `SourceLayerDrawObject` | Video/source layer | done |
| `TextDrawObject` | Text overlay | done |
| `SolidDrawObject` | Solid fill | done |
| `CanvasDrawObject` | Nested canvas | done |

Common base (`MediaForgeDrawObject`):

```csharp
public abstract class MediaForgeDrawObject
{
    public DrawObjectId Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public Transform2D Transform { get; set; }      // position, size, rotation, pivot
    public NormalizedRect? Crop { get; set; }
    public float Opacity { get; set; }
    public BlendMode BlendMode { get; set; }
    // public List<MediaForgeEffect> Effects { get; set; }  // planned — Commit H4
}
```

Draw objects describe **what** to compose and **how** (layout, transform, blend). They do not own GPU resources or Vulkan pipelines.

---

## 6. Effects

**Status:** planned (Commit H4)

Effects are an **ordered pipeline per draw object**, not loose properties on `SourceLayerDrawObject`.

```csharp
public abstract class MediaForgeEffect
{
    public EffectId Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public int Order { get; set; }
}
```

Initial effect types:

- `ChromaKeyEffect` — key color, similarity, smoothness, spill reduction
- `ColorCorrectionEffect` — brightness, contrast, saturation, hue
- `BlurEffect` — radius
- `TransitionEffect` — kind, progress, duration

**Rules:**

- Validator checks finite ranges and schema versions.
- Snapshots deep-clone effects.
- Disabled effects are preserved in the project but skipped at render time.
- Renderer maps effects to shader catalog entries (`mf.*`), not hardcoded branches per object type.

---

## 7. Outputs

**Status:** implemented (partial — output type catalog missing)

An output routes a **canvas** to a **destination** (preview window, file, stream, NDI, etc.).

**Current model:**

```csharp
public sealed class MediaForgeRenderOutput
{
    public RenderOutputId Id { get; set; }
    public string Name { get; set; }
    public CanvasId CanvasId { get; set; }
    public FrameSize OutputSize { get; set; }
    public LayoutMode CanvasLayoutMode { get; set; }
    public ColorRgba LetterboxColor { get; set; }
}
```

**Planned expansion** (Commit H3):

```csharp
public RenderOutputTypeId TypeId { get; set; } = RenderOutputTypes.PreviewWindow;
public int SchemaVersion { get; set; } = 1;
public JsonObject Settings { get; set; } = new();
```

**Planned output types** (`RenderOutputTypes`):

| Type id | Purpose |
|---------|---------|
| `wtk.output.preview.window` | WinForms / native preview surface |
| `wtk.output.offscreen` | Headless GPU texture (nested canvas, tests) |
| `wtk.output.ndi` | NDI output |
| `wtk.output.recording.mp4` | File recording |
| `wtk.output.streaming.rtmp` | Live streaming |
| `wtk.output.virtual.camera` | Virtual camera device |

Typed settings: `PreviewWindowOutputSettings`, `RecordingMp4OutputSettings`, `NdiOutputSettings`, etc., via `RenderOutputSettingsSerializer`.

**Product rule:** Preview vs Program is not a core enum. The user binds any canvas to any output. Preview/Program is a UI workflow convention.

---

## 8. Runtime providers

**Status:** partial

| Component | Role |
|-----------|------|
| `CompositionRuntime` | Registry of live `IVideoFrameProvider` by `SourceId` |
| `IVideoFrameProvider` | Produces latest GPU frame + lease (`TryAcquireLatestFrame`) |
| Desktop duplication | Implemented in `WTK.MediaForge.Capture` |
| Other source types | Not implemented — require provider factory (Commit H7) |

**Planned:** `IMediaSourceProviderFactory` creates providers from `MediaForgeSourceDefinition` + typed settings. The engine starts/stops providers; the product model never references D3D11 or Vulkan types.

---

## 9. Output sinks

**Status:** planned (Commit H7)

Runtime counterparts to `MediaForgeRenderOutput`:

| Product output type | Runtime sink | Status |
|--------------------|--------------|--------|
| Offscreen | `VulkanOffscreenRenderTarget` + binding | scaffolding done |
| Preview window | Win32 swapchain / panel target | POC only (not wired to `IRenderBackend`) |
| NDI / MP4 / RTMP | Encoder/stream sinks | not started |

**Planned:** `IRenderOutputSinkFactory` + `RenderOutputTarget` hierarchy:

```csharp
public abstract class RenderOutputTarget
{
    public abstract RenderOutputTypeId TypeId { get; }
}

public sealed class OffscreenRenderOutputTarget : RenderOutputTarget { ... }
public sealed class WinFormsPreviewRenderOutputTarget : RenderOutputTarget
{
    public nint WindowHandle { get; init; }
}
```

Binding at runtime produces `RenderOutputBindingSnapshot` (already exists) on the render thread.

---

## 10. Editor API

**Status:** planned (Commit H5)

Official mutation API. Serializers may use setters on model types; **application and UI code must not**.

```csharp
public sealed class MediaForgeProjectEditor
{
    public MediaForgeCanvas CreateCanvas(string name, FrameSize size);
    public MediaForgeSourceDefinition CreateSource(string name, IMediaSourceSettings settings);
    public SourceLayerDrawObject AddSourceLayer(CanvasId canvasId, SourceId sourceId, Transform2D transform);
    public TextDrawObject AddText(CanvasId canvasId, string text, Transform2D transform);
    public CanvasDrawObject AddCanvasLayer(CanvasId parentCanvasId, CanvasId nestedCanvasId, Transform2D transform);
    public MediaForgeRenderOutput CreateOutput(string name, CanvasId canvasId, IRenderOutputSettings settings, FrameSize outputSize);
    public void AddEffect(CanvasId canvasId, DrawObjectId objectId, MediaForgeEffect effect);
    public ProjectValidationResult Validate();
}
```

The editor rejects obvious errors (missing source, self-referencing canvas) and always offers `Validate()` before save or engine load.

---

## 11. Engine facade

**Status:** planned (Commit H7 — after product model commits H2–H6)

Single entry point for applications. Replaces manual wiring of runtime, render thread, and providers.

```csharp
public sealed class MediaForgeEngine : IAsyncDisposable
{
    public MediaForgeProject CurrentProject { get; }

    public Task LoadProjectAsync(MediaForgeProject project, CancellationToken cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken);

    public Task ApplyProjectUpdateAsync(
        Action<MediaForgeProjectEditor> edit,
        CancellationToken cancellationToken);

    public Task BindOutputAsync(
        RenderOutputId outputId,
        RenderOutputTarget target,
        CancellationToken cancellationToken);

    public Task UnbindOutputAsync(RenderOutputId outputId, CancellationToken cancellationToken);
}
```

**Lifecycle (target):**

```text
LoadProjectAsync  → validate → build runtime provider map
StartAsync        → start providers → start render thread
ApplyProjectUpdate → editor → validate → new ProjectStateSnapshot → runtime refresh
BindOutputAsync   → enqueue bind on render thread
StopAsync         → stop render thread → stop providers (order per ARCHITECTURE.md)
```

---

## High-level usage (target)

This is the intended developer experience once H2–H7 are complete. **Not available today** except for low-level runtime APIs.

```csharp
// 1. Build project through editor (not raw lists)
var project = new MediaForgeProject();
var editor = new MediaForgeProjectEditor(project);

var desktop = editor.CreateSource("Desktop 1", new DesktopCaptureSourceSettings
{
    OutputIndex = 0,
    CaptureCursor = true
});

var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
editor.AddSourceLayer(canvas.Id, desktop.Id, new Transform2D
{
    Position = new CanvasPoint(0, 0),
    Size = new CanvasSize(1920, 1080)
});

var preview = editor.CreateOutput("Preview", canvas.Id, new PreviewWindowOutputSettings(), new FrameSize(1280, 720));
editor.Validate().ThrowIfInvalid();

// 2. Run through engine facade
await using var engine = new MediaForgeEngine();
await engine.LoadProjectAsync(project, cancellationToken);
await engine.BindOutputAsync(preview.Id, new WinFormsPreviewRenderOutputTarget { WindowHandle = panelHandle }, cancellationToken);
await engine.StartAsync(cancellationToken);

// 3. Live edits
await engine.ApplyProjectUpdateAsync(e =>
{
    e.AddText(canvas.Id, "Live", new Transform2D { ... });
}, cancellationToken);

await engine.StopAsync(cancellationToken);
```

**Today (interim):** use `MediaForgeProjectValidator`, `ProjectStateSnapshotFactory`, `RenderFrameSnapshotFactory`, `CompositionRuntime`, and `MediaForgeRenderThread` directly in tests and integration code. Do not treat this as the final public API.

---

## Snapshot pipeline (product → runtime)

Unchanged from architecture; repeated here because it is the boundary between layers:

```text
MediaForgeProject
  → ProjectStateSnapshotFactory.CreateImmutableSnapshot
ProjectStateSnapshot (deep copy, immutable)
  → RenderFrameSnapshotFactory.Build + TryAcquireLatestFrame
RenderFrameSnapshot (GPU leases, IDisposable)
  → LatestSnapshotBuffer.Publish
MediaForgeRenderThread → IRenderBackend.Submit
```

The product layer never sees `GpuFrameLease`, fences, or keyed mutex keys.

---

## Implementation roadmap (product layer)

P0 GPU lifecycle is **complete**. Product formalization proceeds in this order:

| Commit | Deliverable | Code change |
|--------|-------------|-------------|
| **H1** | This document + ARCHITECTURE cross-links | docs only |
| **H2** | Source type catalog + typed settings + serializer | Composition |
| **H3** | Output type catalog + typed settings + expand `MediaForgeRenderOutput` | Composition |
| **H4** | Effect model + snapshots + validator | Composition |
| **H6** | Graph validation (cycles, depth 8, schema versions) | Composition |
| **H5** | `MediaForgeProjectEditor` | Composition |
| **H7** | `MediaForgeEngine` skeleton + provider/sink factories | Composition |

**Only after H7:**

- Real sources: webcam, NDI, RTSP, MP4, etc.
- Real outputs: preview binding, NDI out, MP4, streaming
- Visual compositing pipeline (source layer → offscreen → outputs)

---

## Readiness checklist

| Capability | Available | Clear contract |
|------------|-----------|----------------|
| Project / Canvas / DrawObjects | yes | yes |
| SourceDefinition + SourceLayer | yes | partial (catalog incomplete) |
| RenderOutput | yes | partial (type id missing) |
| Effects | no | yes (this doc) |
| Source type catalog | no | yes |
| Output type catalog | no | yes |
| Typed settings | no | yes |
| Project editor API | no | yes |
| Engine facade | no | yes |
| Provider factory | no | yes |
| Output sink factory | no | yes |

**Verdict:** foundation exists; product contracts are now documented. Implementation of H2–H7 is required before feature teams add media types ad hoc.
