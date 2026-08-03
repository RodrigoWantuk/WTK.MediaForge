# WTK MediaForge Product Model

This document defines what users and applications author, save, validate, route, and operate.

Runtime ownership and physical execution live in [`../ARCHITECTURE.md`](../ARCHITECTURE.md). Public API exposure lives in [`PUBLIC_API.md`](PUBLIC_API.md). Active implementation order lives in [`ROADMAP_CURRENT.md`](ROADMAP_CURRENT.md).

If a proposed feature does not fit this model, update the model deliberately before implementing it. Do not create a parallel product concept inside a host, adapter, renderer, or test.

## Canonical root

`MediaForgeProject` is the only persisted product root.

```text
MediaForgeProject
  -> SourceDefinitions
  -> Canvases / Scenes
     -> DrawObjects / Layers
     -> Effects
  -> RenderOutputs
     -> Sink intent and routing
  -> Audio graph
     -> Sources, Nodes, Connections, Buses, Routes, Sinks
```

Project JSON may contain:

- schema/version metadata;
- stable logical ids;
- typed source and output settings;
- canvases, draw objects, transforms, effects, and nested-canvas bindings;
- output routing and transitions;
- audio graph definitions;
- user metadata and extension data.

Project JSON must not contain:

- runtime leases;
- native handles;
- Vulkan or D3D11 objects;
- command buffers, fences, semaphores, or queues;
- provider, encoder, decoder, sink-worker, or presenter state;
- connection/session credentials;
- SDP, ICE, invitation, bearer, TURN, or other runtime secrets.

## Product/runtime boundary

| Concern | Product layer | Runtime/physical layer |
|---|---|---|
| Editable state | `MediaForgeProject`, builders, editors | immutable snapshots and sessions |
| Sources | reusable typed definitions | providers, hardware capture/decode, frame leases |
| Scenes | canvases, ordered draw objects, effects | resolved versions, render snapshots, physical operations |
| Outputs | enabled state, route, dimensions, format, transition, typed settings | output surfaces, presenters, exporters, encoders, packet sinks |
| Audio | graph definitions, buses, routes, sink intent | compiled plans, pooled blocks, callbacks, native adapters |
| Diagnostics | validation and public health contracts | backend events, counters, failure/recovery state |

Forbidden shortcuts:

- source-specific draw-object classes such as `WebcamDrawObject` or `RtspDrawObject`;
- source, NDI, encoder, network, file, or sink logic inside the Vulkan renderer;
- chroma key or other effects as ad-hoc source properties;
- transitions implemented as permanent layer effects;
- Studio-specific persisted project formats;
- public APIs that require render threads, backend factories, physical plans, snapshots, GPU leases, or native handles;
- platform implementation references from portable product projects.

## Authoring API

Normal callers use:

- `MediaForgeProjectBuilder`;
- `MediaForgeProjectEditor`;
- typed source/output factories and settings;
- layer builders;
- serializer, loader, migrator, and validator;
- package and preset import/export APIs.

Direct list mutation may exist for serialization and internal implementation, but it is not the preferred external authoring workflow.

All project mutation is validated before replacing engine-owned state.

## Sources

A source is defined once and can be referenced by multiple layers and scenes.

A source:

- produces leased frames or static GPU assets;
- owns no scene placement;
- does not render;
- does not know which canvases or outputs consume it;
- does not invoke sinks;
- has explicit runtime capability and lifecycle.

Current source-setting contracts include:

- desktop capture;
- window capture;
- webcam;
- static image;
- animated image;
- Lottie;
- video file;
- RTSP;
- IP camera;
- NDI input;
- generated source;
- Remote Scene input.

The presence of a setting contract does not mean a physical adapter is available.

## Draw objects and layers

Current canonical draw-object kinds:

- source layer;
- text;
- solid;
- nested canvas.

Common visual properties include:

- enabled/visible state;
- transform and pivot;
- position and dimensions;
- rotation;
- crop;
- opacity;
- blend mode;
- ordered effects;
- stable logical identity and user-facing name.

Draw objects describe intent. They never own native or GPU resources.

A source layer references a reusable source definition. Multiple source layers may reference the same source with different placement and effects.

## Canvases and scenes

`MediaForgeCanvas` is the canonical scene object.

Public APIs and Studio may use “scene” as product terminology, but it remains an ergonomic name for canvas rather than a second graph type.

A canvas:

- has stable logical identity;
- has dimensions, frame rate, color/background configuration, metadata, and ordered draw objects;
- may be routed to multiple outputs;
- may be used as a layer in another canvas;
- participates in versioning and dependency resolution.

Nested-canvas rules:

- direct and transitive cycles are invalid;
- maximum nesting depth is bounded by the engine contract;
- disabled nested layers do not acquire/render their internal content;
- version binding is explicit;
- identical resolved content may be reused where physical configuration permits;
- logical `CanvasId` remains stable across versions and bindings.

## Scene editing modes

Scene editing semantics are engine behavior.

### Live

- A Live session targets published state.
- Mutations are applied as one validated transaction.
- Successful changes become visible to published outputs on subsequent frames.
- Rejected mutations leave the last valid published version intact.
- Leaving Live closes/discards the runtime edit session deterministically.

### Apply

- An Apply session creates an isolated draft.
- Draft mutations are visible only to that draft binding/session.
- Published outputs continue rendering the published version.
- `ApplySceneDraftAsync` validates and publishes the draft.
- `DiscardSceneDraftAsync` removes the draft without changing published state.

### Version bindings

Nested canvases use one of these semantic bindings:

- published;
- draft session;
- explicit version.

Explicit versions support transitions and historical resolution. They do not create new logical canvas ids.

### Apply propagation

Applying a canvas draft computes:

- the changed canvas version;
- direct parent canvases;
- transitive parent canvases;
- affected outputs;
- route-owned transition policy.

Studio and other hosts must use engine-reported affected output ids. They must not infer a second dependency graph.

## Effects

Effects are ordered product objects in explicit stacks.

Supported or declared effects include:

- chroma key;
- color correction;
- blur;
- later approved effects and masks.

Effect capability metadata defines:

- allowed scope;
- input/output formats;
- color/alpha behavior;
- pass class;
- temporal state;
- mask support;
- hardware requirements.

The validator rejects invalid placement before physical planning.

A declared effect contract is not renderer support. Renderer support requires implementation, tests, and the applicable proof.

Transitions are not effects. Cut/Fade and future transitions remain output-route behavior over old/new scene-version graphs.

## Render outputs

A render output routes one canvas to completed output surfaces and sinks.

```text
Canvas
  -> RenderOutput
  -> completed GPU output surface
  -> one or more sinks
```

An output contains canonical authoring state such as:

- stable id and name;
- enabled state;
- routed canvas;
- dimensions and frame rate;
- layout and letterbox behavior;
- color/output format;
- transition policy;
- typed preview/recording/streaming settings.

Disabled outputs remain persisted and editable but do not create runtime routes.

Current output-setting contracts include:

- offscreen;
- preview;
- MP4 recording;
- encoded file;
- RTMP;
- SRT;
- RTSP;
- HLS;
- virtual camera;
- NDI output;
- Remote Scene output.

The presence of a contract does not imply runtime availability.

## Sinks

Sinks consume completed output.

They:

- never call or trigger the renderer;
- use explicit bounded queues and backpressure policy;
- retain independent lifecycle and failure state;
- receive GPU output leases or validated encoded-packet leases according to transport kind;
- release ownership deterministically.

`CpuReadbackSink` is debug/test/sample only.

Primary preview uses a GPU presenter. Recording and streaming consume validated hardware-encoded packets.

## Routing and shared work

Target dependency direction:

```text
Outputs/Sinks
  -> RenderOutput
  -> Canvas
  -> DrawObjects
  -> Sources
  -> Effects
```

Expected reuse:

- acquire one source frame once per frame/version context;
- reuse source/effect work when placement-independent and physically compatible;
- reuse a canvas for matching resolved version, size, format, and output configuration;
- split only required presentation/layout passes;
- fan out one completed output to multiple sinks;
- share conversion and encoder only when the complete encoder identity matches.

Compatibility is intentionally separated into:

- rendered-pixel identity;
- encoder identity;
- sink identity.

Destination/backpressure differences must not alter pixel or encoder identity. Codec profile, level, dimensions, FPS, bitrate, GOP, pixel format, and color differences must prevent unsafe sharing.

## Physical RenderGraph

The physical graph is the production execution contract between immutable render state and native backend work.

It represents:

- source acquisition;
- transforms and placement-dependent operations;
- effect intermediates;
- primitive and source layers;
- canvas composition;
- nested canvases;
- transitions;
- output passes;
- fan-out;
- encoded dispatch;
- physical resource ownership.

Production Vulkan submission requires a validated physical plan. It must not discover or reconstruct missing product behavior independently.

Current implementation already binds source imports and encoded dispatch to typed physical operations. Remaining roadmap work closes exclusive authority for every temporary/effect resource and sustains that behavior under long-running in-flight load.

## Audio

Audio is a project-global graph independent of the visual scene graph.

A source captures or generates once. Nodes process. Buses mix. Routes bind buses to sinks. Video outputs may select audio routes but do not own physical audio devices.

Portable audio definitions never contain native devices, callback buffers, threads, or handles.

Current portable runtime includes deterministic pooled processing and in-memory route fan-out. Physical capture, playback, encode, and A/V mux remain separate platform-adapter work.

See [`AUDIO_ARCHITECTURE.md`](AUDIO_ARCHITECTURE.md).

## Package and preset serialization

Supported package concepts include:

- full project;
- scene package with dependencies;
- canvas/layout preset;
- source preset;
- output preset;
- effect preset.

Import modes may include:

- replace project;
- merge as new scene;
- merge presets only;
- dry-run validation.

Import builds and validates a candidate before returning it. Failed import and dry-run never mutate current state.

Secret-safe export is the default for output and remote configuration.

## Engine facade

`MediaForgeEngine` is the public runtime facade.

Its product responsibilities include:

- canonical project load/update;
- lifecycle;
- scene edit sessions;
- sink/output activation through public contracts;
- capability and health observation;
- deterministic stop/dispose;
- typed validation, unsupported-feature, and runtime failure reporting.

Hosts must not manually assemble internal composition/runtime services.

## Studio projection

Studio is a projection and controller over canonical project and engine state.

It may maintain UI-specific selection, viewport, undo/redo, dialog, layout, and draft presentation state, but it must not persist a competing media model.

Studio save:

1. applies UI edits to a detached canonical clone;
2. validates the candidate;
3. writes a temporary file in the destination directory;
4. atomically replaces the destination;
5. advances in-memory canonical session state only after replacement succeeds.

Fields not represented by the current UI remain intact.

## Capability and availability rule

For every source, effect, output, sink, adapter, or product workflow:

- model contract answers “can the project describe it?”;
- implementation answers “does code exist?”;
- proof answers “did the required physical path pass?”;
- capability snapshot answers “is it available now on this environment?”

Do not collapse those questions into one status.
