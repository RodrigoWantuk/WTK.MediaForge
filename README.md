# WTK MediaForge

**WTK MediaForge** is a high-performance audio and video composition solution focused on real-time media processing, hardware acceleration, and low system overhead.

The project is designed as a GPU-first media compositor: instead of relying heavily on CPU-based frame processing, WTK MediaForge aims to use hardware acceleration together with Vulcan whenever possible to reduce CPU usage, avoid unnecessary raw video transfers through system RAM, and keep the host machine responsive even when working with complex scenes.

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

## Technology Direction

The current technical direction is:

* **.NET 8**
* **WinForms** for the initial desktop host application
* **Silk.NET** for Vulkan bindings
* **Vulkan** for GPU-based rendering and composition
* **Vortice.Windows** for D3D11/DXGI interop
* **Desktop Duplication API** for the first desktop capture implementation

Future media processing and output modules may use FFmpeg through controlled LGPL-compatible integration.

## License

WTK MediaForge is source-available under the PolyForm Noncommercial License 1.0.0.

You may use, study, modify, and run this project for personal, educational, research, evaluation, hobby, and other non-commercial purposes.

Commercial, industrial, SaaS, broadcast, resale, consulting, integration into paid products or services, production use, or any revenue-generating use requires a separate written commercial license from the author.

For commercial licensing, contact:

[rodrigowantuk@gmail.com](mailto:rodrigowantuk@gmail.com)

Required Notice: Copyright Rodrigo Wantuk.

## Third-Party Components

This project may depend on third-party libraries and components with their own licenses.

Third-party licenses are not replaced or overridden by the WTK MediaForge license. Each dependency remains governed by its respective license.
