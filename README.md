# WTK MediaForge

WTK MediaForge is a GPU-first media composition engine and native Avalonia Studio for real-time capture, composition, preview, recording, and streaming.

The project is designed so that continuous uncompressed video remains on GPU-backed surfaces from capture or hardware decode through Vulkan composition and hardware encode/presentation. There is no software video codec fallback on product paths.

## Current status

The repository contains a substantial Windows implementation and portable cross-platform architecture:

- canonical project, source, canvas, layer, effect, output, capability, and scene-editing contracts;
- transactional engine lifecycle and deterministic resource ownership;
- Vulkan composition with nested canvases, transforms, crop, opacity, blend, text, solid layers, chroma key, color correction, blur, Cut, and Fade;
- a validated physical RenderGraph covering source acquisition, effects, canvases, outputs, fan-out, transitions, and encoded dispatch;
- Windows desktop, window, webcam, static-image, and MP4 input paths;
- Windows GPU export, Media Foundation hardware H.264 encode, MP4 recording, and RTMP publishing;
- explicit Live and Apply scene editing with versioned nested canvases;
- native Avalonia Studio with canonical persistence, scene/layer editing, output routing, runtime lifecycle, and proof-gated recording/streaming controls;
- portable audio graph, pooled processing, mixing, meters, fixed delay, and bounded in-memory Program Mix routes;
- Remote Scene signaling and transport contracts;
- mandatory Windows and Linux CI for portable architecture.

The main product gaps are hosted-preview promotion, sustained hardware qualification, remaining Physical RenderGraph ownership closure, physical audio adapters, Linux/macOS media adapters, and Remote Scene media.

See [`docs/ROADMAP_CURRENT.md`](docs/ROADMAP_CURRENT.md) for current reality and execution order.

## Active functional milestone

The next integrated delivery checkpoint is a functional public API and Avalonia Studio using the same production engine path.

The milestone includes:

- a public API quickstart;
- native hosted GPU preview;
- nested scenes;
- Live and Apply editing;
- proof-gated MP4 and RTMP outputs;
- real Studio source/output editing;
- deterministic shutdown and resource baseline return.

See the functional API/Studio milestone document.

The milestone is used only as a delivery checkpoint. It does not relax the final architecture, GPU transport law, capability truth, cross-platform boundaries, or validation requirements.

## Core principles

### GPU-first video

- Continuous uncompressed video remains in GPU memory on product paths.
- Capture/decode produces GPU-backed leases.
- Composition and effects execute through Vulkan.
- Encode and preview consume GPU-backed output.
- Software decode/encode and raw-video pipes are prohibited as product fallback.

### Explicit capability truth

A feature is available only when the real adapter, driver, API, implementation, output surface, and required proof chain support it.

Missing hardware or incomplete proof is reported with a concrete reason. Model presence, prototype code, nominal GPU names, and skipped tests are not capability evidence.

### Modular product architecture

```text
MediaForgeProject
  -> reusable sources
  -> canvases/scenes
     -> source, text, solid, or nested-canvas layers
     -> ordered effects
  -> render outputs
     -> preview and/or encoded sinks
  -> global audio graph
```

Sources do not render. Sinks do not request rendering. Native resources remain in platform projects.

### Cross-platform contract

- Windows and Linux are mandatory build/test targets.
- Windows currently owns the physical production media path.
- Linux and macOS physical adapters remain planned.
- Portable projects never depend on platform implementation projects.

## Technology

- .NET 8
- Avalonia UI
- CommunityToolkit.Mvvm
- Silk.NET Vulkan
- Vortice D3D11/DXGI
- Windows Graphics Capture
- Desktop Duplication API
- Media Foundation hardware decode/encode

WinForms remains a legacy/diagnostic host, not the primary product UI.

## Documentation

Start with [`docs/README.md`](docs/README.md).

Primary normative documents:

- [`docs/ROADMAP_CURRENT.md`](docs/ROADMAP_CURRENT.md)
- [`docs/AI_CONTEXT.md`](docs/AI_CONTEXT.md)
- [`docs/PRODUCT_MODEL.md`](docs/PRODUCT_MODEL.md)
- [`docs/PUBLIC_API.md`](docs/PUBLIC_API.md)
- [`ARCHITECTURE.md`](ARCHITECTURE.md)
- [`docs/GPU_MEDIA_SUPPORT_MATRIX.md`](docs/GPU_MEDIA_SUPPORT_MATRIX.md)
- [`docs/AUDIO_SUPPORT_MATRIX.md`](docs/AUDIO_SUPPORT_MATRIX.md)
- [`docs/BUILD_AND_RELEASE.md`](docs/BUILD_AND_RELEASE.md)

Files under `docs/history` are non-normative evidence.

## Validation

Baseline:

```powershell
dotnet restore .\WTK.MediaForge.sln --locked-mode
dotnet build .\WTK.MediaForge.sln --no-restore --configuration Release
dotnet test .\WTK.MediaForge.sln --no-restore --no-build --configuration Release
.\scripts\test.ps1 -Tier Fast
```

Hardware-sensitive work:

```powershell
.\scripts\test.ps1 -Tier Gpu
.\scripts\verify-engine-readiness-v14.ps1 -RequireHardwareMedia
```

Studio UI:

```powershell
.\scripts\verify-studio-ui-visual-qa.ps1
```

Release entrypoint:

```powershell
.\scripts\verify-final-gate.ps1 -RequireHardwareMedia
```

## License

WTK MediaForge is source-available under the PolyForm Noncommercial License 1.0.0.

You may use, study, modify, and run the project for personal, educational, research, evaluation, hobby, and other non-commercial purposes.

Commercial, industrial, SaaS, broadcast, resale, consulting, integration into paid products or services, production use, or any revenue-generating use requires a separate written commercial license from the author.

For commercial licensing, contact [rodrigowantuk@gmail.com](mailto:rodrigowantuk@gmail.com).

You can also support the project through [Buy Me a Coffee](https://buymeacoffee.com/rodrigowantuk).

Required notice: Copyright Rodrigo Wantuk.

## Third-party components

Third-party dependencies retain their own licenses. The MediaForge license does not replace or override those terms.
