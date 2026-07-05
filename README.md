# WTK MediaForge

**WTK MediaForge** is a high-performance audio and video composition solution focused on real-time media processing, hardware acceleration, and low system overhead.

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
  Capture, decoding, rendering, composition, and encoding should use hardware acceleration when available.

* **Modular architecture**
  Capture sources, rendering backends, composition logic, media processing, and output modules should be isolated and replaceable.

* **Real-time control**
  Scenes, overlays, text, layouts, and media sources should be adjustable while the pipeline is running.

* **Studio application**
  In addition to the engine/API surface, the project now has a planned Avalonia-based Studio shell for users who want a complete desktop tool for scenes, sources, layers, effects, outputs, diagnostics, and future audio workflows.

## Technology Direction

The current technical direction is:

* **.NET 8**
* **Avalonia UI** for the planned cross-platform Studio application
* **WinForms** only as an initial Windows test harness / legacy POC host
* **Silk.NET** for Vulkan bindings
* **Vulkan** for GPU-based rendering and composition
* **Vortice.Direct3D11 / Vortice.DXGI** for D3D11/DXGI interop
* **Desktop Duplication API** for the first Windows desktop capture implementation

Future media processing and output modules may use FFmpeg through controlled LGPL-compatible integration.


## Studio UI Direction

WTK MediaForge Studio is the planned desktop product shell for users who do not want to consume the engine through APIs directly.

The approved first UI direction is an Avalonia dark-theme workbench with:

* project explorer for scenes, sources, outputs, presets, and packages;
* central preview/canvas area with zoom, grid/safe-area controls, and layer selection overlays;
* contextual inspector for scene/source/layer/effect/output properties;
* bottom workbench tabs for layers, effects, timeline, diagnostics, performance, output monitor, and future audio mixer;
* compact runtime status for engine, backend, FPS, frame time, dropped frames, outputs, and warnings.

The React/Lovable prototype is a visual reference only. The Studio implementation must be native Avalonia/MVVM and must not embed React, WebView, Electron, or browser runtime dependencies.

See:

* `docs/UI_STUDIO_DESIGN.md`
* `docs/UI_REACT_TO_AVALONIA_MAPPING.md`
* `docs/UI_IMPLEMENTATION_PLAN.md`
* `docs/UI_ACCEPTANCE_CHECKLIST.md`
* `docs/STUDIO_UI_RECOVERY_PLAN.md`
* `docs/STUDIO_UI_VISUAL_ACCEPTANCE.md`

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
