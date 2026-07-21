# WTK MediaForge

**WTK MediaForge** is a high-performance video-first media composition engine focused on real-time processing, hardware acceleration, and low system overhead. Audio routing, mixing, and muxing are planned future product areas, not part of the current engine path.

The project is designed as a GPU-first media compositor: instead of relying heavily on CPU-based frame processing, WTK MediaForge aims to use hardware acceleration together with Vulkan whenever possible to reduce CPU usage, avoid unnecessary raw video transfers through system RAM, and keep the host machine responsive even when working with complex scenes.

The long-term goal is to provide a lightweight, modular, and extensible media composition engine for scenarios such as live production, screen capture, picture-in-picture layouts, scene composition, overlays, real-time text, audio/video routing, recording, and streaming.

## Project Goals

WTK MediaForge is being built around a few core principles:

* **GPU-first composition**
  Video frames should be processed, transformed, composed, and rendered primarily on the GPU.

* **Low CPU overhead**
  The CPU should coordinate the pipeline, not process every raw video frame.

* **Reduced RAM bandwidth usage**
  The project aims to avoid unnecessary movement of uncompressed video frames through system RAM whenever possible.

* **Hardware acceleration**
  Capture, decoding, rendering, composition, and encoding use hardware acceleration on product paths. Continuous video decode/encode is hardware/GPU path or unavailable; there is no software fallback for product media.

* **Modular architecture**
  Capture sources, rendering backends, composition logic, media processing, and output modules should be isolated and replaceable.

* **Real-time control**
  Scenes, overlays, text, layouts, and media sources should be adjustable while the pipeline is running.

* **Studio application**
  The Avalonia Studio has a real runtime/design composition boundary, capability probing, an editable overlay, and canonical project sessions that preserve settings not exposed by the UI. Saves validate a clone and atomically replace the file before committing session state. Native preview/output controls remain capability-gated.

## Technology Direction

The current technical direction is:

* **.NET 8**
* **Avalonia UI** for the Studio application; cross-platform is the product goal,
  while the current production media implementation and qualified Studio host are Windows
* **WinForms** only as an initial Windows test harness / legacy POC host
* **Silk.NET** for Vulkan bindings
* **Vulkan** for GPU-based rendering and composition
* **Vortice.Direct3D11 / Vortice.DXGI** for D3D11/DXGI interop
* **Desktop Duplication API** for the first Windows desktop capture implementation

FFmpeg is not part of the first hardware MP4/RTMP product path. Any future FFmpeg/libav usage must pass the dedicated license and GPU media transport review, and may only operate on encoded packets, containers, metadata, or bitstream data.

Product media availability is proof-gated. Continuous decode/encode and real
media I/O features must pass the v14 hardware media proofs (`HardwareMediaProof`
entries for render-to-encode, hardware encode, MP4 recording, hardware decode,
decode-to-render, MP4 input/output, webcam input, RTMP network output, and NDI
input/output) before they can be advertised as supported. The current official
gate is `./scripts/verify-engine-readiness-v14.ps1`; release hardware
validation uses `./scripts/verify-engine-readiness-v14.ps1 -RequireHardwareMedia`.
Real Windows implementations exist for MP4 recording, RTMP, MP4 input, webcam,
desktop capture, and preview, but runtime availability remains adapter/proof
dependent. Preview is temporarily experimental until hosted resize and
fence-timeout recovery pass. NDI Standard SDK runtime detection and source discovery
exist on Windows, and licensed runtime DLLs can be packed as native assets, but
NDI video input/output remain blocked until GPU-safe product proofs pass.

Remote Scene signaling and platform-neutral contracts are implemented, but
Remote Scene media is not available. The checked-in native WebRTC target is an
ABI contract test with an unavailable backend; Direct and TURN physical GPU
proofs have not been produced. Signaling coordinates SDP/ICE, TURN relays
encrypted packets, and neither component is the media compositor/transport proof.


## Studio UI Direction

WTK MediaForge Studio is the desktop product shell for users who do not want to consume the engine through APIs directly.

The approved current UI direction is an Avalonia dark-theme mock workbench with:

* a project model centered on scenes/canvases, reusable sources, scene layers, scene/layer effects, and routed outputs;
* a scenes-first left navigation with source/output actions close to the relevant lists;
* a dominant editable canvas mock with zoom, pan, layer hit testing, move/resize handles, grid/safe-area controls, and a separate overlay layer for future GPU preview integration;
* contextual properties for scene, source, layer, effect, and output routing settings;
* bottom workbench content limited to the main user workflow: layers, effects, and scene outputs;
* production/output cards that make scene-to-sink routing and transitions visible without exposing engine, Vulkan, D3D11, command buffers, fences, or native handles.

Diagnostics, performance details, and other low-level runtime information belong in advanced tooling, not in the primary user workflow. The scene editor overlay remains mock-rendered while native preview is experimental; runtime features are shown only from actual capability snapshots.

The React/Lovable prototype is a visual reference only. The Studio implementation must be native Avalonia/MVVM and must not embed React, WebView, Electron, or browser runtime dependencies.

See:

* `docs/UI_STUDIO_DESIGN.md`
* `docs/UI_IMPLEMENTATION_PLAN.md`
* `docs/UI_ACCEPTANCE_CHECKLIST.md`
* `docs/BUILD_AND_RELEASE.md`
* `docs/SIGNALING_DEPLOYMENT.md`
* `docs/KNOWN_LIMITATIONS.md`

## License

WTK MediaForge is source-available under the PolyForm Noncommercial License 1.0.0.

You may use, study, modify, and run this project for personal, educational, research, evaluation, hobby, and other non-commercial purposes.

Commercial, industrial, SaaS, broadcast, resale, consulting, integration into paid products or services, production use, or any revenue-generating use requires a separate written commercial license from the author.

For commercial licensing, contact:

[rodrigowantuk@gmail.com](mailto:rodrigowantuk@gmail.com)

Also, if you like or found this Project useful, you can [buy me a coffee](https://buymeacoffee.com/rodrigowantuk)!

Required Notice: Copyright Rodrigo Wantuk.

## Third-Party Components

This project may depend on third-party libraries and components with their own licenses.

Third-party licenses are not replaced or overridden by the WTK MediaForge license. Each dependency remains governed by its respective license.
