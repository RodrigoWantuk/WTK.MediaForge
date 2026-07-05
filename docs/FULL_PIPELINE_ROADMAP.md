# Full Pipeline Roadmap

This document describes the target product pipeline. `docs/ROADMAP_CURRENT.md`
remains the mandatory execution order for active work. This file is the product
map for scene routing, source/output growth, serialization, effects, and future
audio support.

## Product Target

WTK MediaForge is a hardware-first, GPU/VRAM-first media engine for live
production, scene composition, preview/program routing, recording, streaming,
and future audio mux/mix.

The core product model is:

```text
Project
  -> Sources
  -> Scenes/Canvases
  -> RenderOutputs
  -> Sinks
```

`MediaForgeCanvas` remains the canonical internal scene object. Public APIs may
use `Scene` as ergonomic terminology, but it must not introduce a competing
render primitive.

## Multi-Scene Routing

The engine must support multiple canvases/scenes running in parallel. A user can
route any scene to any output independently:

```text
Scene A -> Preview panel A
Scene B -> Preview panel B
Scene C -> Program output
Scene C -> Recording sink
Scene D -> Debug/offscreen sink
```

Sources are project-level objects and can feed any number of scenes/layers.
Effects that are independent of layer placement can be shared by render-graph
nodes instead of recomputed per layer. A rendered canvas at the same size,
configuration, and version can be reused across outputs; only output-fit,
letterbox, or presentation passes should split by output.

Sinks never trigger rendering directly. They subscribe to completed
`RenderOutput` frames and consume surface leases or sync-aware frames after the
renderer has produced the output.

## Render Graph

The target per-frame graph is:

```text
Outputs/Sinks
  -> RenderOutput
  -> Canvas/Scene
  -> DrawObjects
  -> Sources
  -> Effects
```

The compiler builds a DAG and deduplicates stable nodes:

- source frame acquisition: once per source per frame
- reusable source effect chain: once for identical source/config/effects
- canvas render: once for identical canvas size/config/version
- output pass: once per output size/layout/target
- fanout: one completed output frame can feed many sinks

The current repository contains the first internal render-graph planning
foundation for these dedupe rules. It is not yet the Vulkan execution planner.

## Public Authoring API

High-level authoring should remain simple and typed:

```csharp
var project = new MediaForgeProjectBuilder()
    .Scene("Program", 1920, 1080, out var program)
    .Scene("Preview", 1280, 720, out var preview)
    .WebcamSource("Camera", "Logitech BRIO", out var camera)
    .DesktopSource("Desktop", 0, out var desktop)
    .SourceLayer(program, camera)
        .Pip(1380, 720, 480, 270)
        .AddChromaKey(keyColor)
        .Done()
    .SourceLayer(program, desktop)
        .Fit()
        .Done()
    .PreviewOutput("Preview A", preview, 1280, 720, out var previewOutput)
    .RtmpOutput("Program Stream", program, "rtmp://...", streamKey, out var streamOutput)
    .Route(program, streamOutput)
    .BuildValidated();
```

The target API vocabulary:

- `Project.Scene(...)`
- `Sources.Desktop/Webcam/MediaFile/Image/AnimatedImage/Ndi/Rtsp/IpCamera(...)`
- `Scene.Layer(...)`
- `Layer.Pip/Mosaic/Fit/Fill/ChromaKey(...)`
- `Outputs.Preview/RecordMp4/Rtmp/Ndi/VirtualCamera(...)`
- `Route(scene, output/sink)`

The current builder contains the first `Scene`, source helper, output helper,
route, and chroma-key layer helpers. Future helpers must compile to the same
stable project model instead of bypassing validation.

## Serialization Packages

JSON is a product contract, not a dump of runtime state. Serializable packages:

- `MediaForgeProject`: full project save/load.
- `MediaForgeScenePackage`: one root canvas/scene plus nested canvases, layers,
  effects, referenced sources, routed outputs, and metadata.
- `MediaForgeCanvasPreset`: reusable layout, PiP, mosaic, or canvas template.
- `MediaForgeSourcePreset`: source definition without runtime handles and
  without secrets unless explicitly included.
- `MediaForgeOutputPreset`: output profile with secret-safe export by default.
- `MediaForgeEffectPreset`: reusable effect chain.

JSON may contain:

- schema version
- stable ids
- type ids
- typed settings
- transforms, crop, opacity, blend, effects
- canvas graph
- output routes
- metadata

JSON must not contain:

- runtime leases
- native handles
- D3D11/Vulkan objects
- command buffers
- fences
- backend worker state
- sink queues
- secret output credentials unless export options explicitly allow them

Import must validate schema, ids, canvas cycles, missing source references,
unsupported types, and migrations before mutating engine state.

Supported import modes:

- full replace project
- merge as new scene
- merge presets only
- dry-run validation

## Source Roadmap

Source API rules:

- a source produces frames; it does not render
- a source does not know about canvas, layer, output, or sink
- every frame has clear lease/lifetime ownership
- one source can feed many layers/scenes
- no-frame/failure conditions must not crash the renderer
- live sources use `KeepLatest` or another explicit policy
- timeline/file sources are separate from live sources

Implementation order:

1. Desktop/window reliability.
2. Webcam.
3. Static image.
4. Animated image: GIF, APNG, WebP.
5. Lottie raster source.
6. Media file timeline/MP4.
7. RTSP/IP camera.
8. NDI input.

File/timeline sources require `MediaTimelineClock`, seek, pause, playback rate,
timestamp frame selection, loop/end behavior, and lease lifetime tests.

## Output Roadmap

Sink API rules:

- a sink consumes an already rendered `RenderOutput`
- a sink never forces a new render
- multiple sinks can consume the same output
- sink callbacks stay off the render thread
- slow sinks use honest backpressure
- every frame delivered to a sink has clear lease/lifetime ownership

Implementation order:

1. Stabilize `PreviewPanelSink`.
2. Encoded file output.
3. RTMP/SRT streaming.
4. NDI output.
5. Virtual camera.

## Composition And Effects Roadmap

Finish product composition primitives before broad media I/O:

1. Full transform, crop, and rotation.
2. Text rendering.
3. Blur.
4. Color correction.
5. Transitions.
6. Effect-chain passes and pooled intermediate targets.
7. PiP helpers.
8. Mosaic helpers.
9. Cached reusable effect intermediates.

`ChromaKeyEffect` remains the first accepted real source-layer effect. Unsupported
effects, routes, sources, outputs, or platform capabilities must produce
explicit diagnostics, never silent fallback.

## Native Media Bridge

The final media path should use a portable native bridge:

- Windows: D3D11/D3D11VA/Media Foundation only when needed.
- Linux: VAAPI/DRM/Vulkan Video/CUDA where available.
- macOS: VideoToolbox/Metal/CVPixelBuffer bridge.

CPU readback remains debug/sample/validation only. The primary path is hardware
decode/capture to GPU texture, Vulkan composition, and hardware encode/output.

### GPU media transport law

Uncompressed continuous video must not traverse CPU/RAM on the product path.

```text
Encoded media -> hardware decode -> GPU surface -> composition -> GPU surface
  -> hardware encode -> encoded packets -> mux/stream/file
```

Prohibited: GPU readback -> raw CPU frame -> CPU encoder/streamer.

Static images: `StaticCpuAsset` load-time CPU decode, upload to GPU, release CPU copy.

Exceptions for continuous raw CPU video require `RawCpuVideoFrameException` registration.

FFmpeg is not used in the first hardware MP4/RTMP MVP.

Windows recording MVP: Media Foundation hardware MFT H.264, packets-only muxer,
Vulkan -> D3D11 encoder surface export (Commit 06 gate).

SRT output: Planned/blocked until license and transport design review.

## Future Audio Contract

Audio is future-only until the video pipeline is stable. The product model should
reserve space for:

- `AudioSourceDefinition`
- `AudioBus`
- `AudioMixer`
- `AudioClock`
- mux sync metadata
- output audio compatibility

Do not implement audio capture, mix, mux, or equalization in the active video
pipeline track.

## Test Strategy

Required future coverage:

- multi-scene routing: scene to multiple sinks renders once
- same source across scenes acquires once
- same canvas across outputs reuses canvas pass
- output size differences redo only output pass
- full project roundtrip
- scene export/import
- preset import
- secret-safe output export
- migrations
- missing-reference diagnostics
- canvas cycle rejection
- dry-run import without mutation
- source lease lifetime and no-frame/failure behavior
- timeline seek/loop/end behavior
- live source backpressure and reconnect diagnostics
- multi-sink fanout and slow sink behavior
- encoded/stream output lifecycle and reconnect diagnostics
- renderer pixel tests for text, blur, color correction, transitions, PiP,
  mosaic, crop, rotation, effect ordering, and cached intermediate correctness
