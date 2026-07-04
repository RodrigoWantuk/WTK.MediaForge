# Current Roadmap

This roadmap is mandatory. Do not choose a different order inside the active
track. Historical acceptance details live in the CP2/CP3 acceptance reports.
Long-term product planning lives in `docs/FULL_PIPELINE_ROADMAP.md`.

## Current Status

Complete foundations:

- P0 GPU lifecycle hardening.
- Engine transactional/shutdown hardening.
- Product model and public API foundations.
- Source runtime/buffer foundation.
- Public sink queue/fanout foundation.
- CP1 visual correctness for the first source/offscreen path.
- CP2 multi-layer Vulkan composition.
- CP3 solid layer, nested canvas, and first `ChromaKeyEffect`.
- First public visual sink through `CpuReadbackSink` for debug/sample/validation.
- `PreviewPanelSink` lifecycle hardening; still experimental pending local reliability.
- Intermediate target pool and Vulkan readback staging pool.
- Full pipeline product foundation: scene/source/output helpers, multi-scene routing contracts, package/preset serialization contracts, and render-graph planning tests.

Acceptance records:

- `docs/CP2_ACCEPTANCE.md`
- `docs/CP3_SOLID_ACCEPTANCE.md`
- `docs/CP3_NESTED_ACCEPTANCE.md`
- `docs/CP3_CHROMA_ACCEPTANCE.md`

## Active Commit Order

1. **Preview local reliability milestone**
   - Run `PreviewPanelSink` for extended periods without obvious GPU leaks.
   - Validate repeated attach/detach, panel resize, and stop/start cycles.
   - Keep `PreviewPanelSink` experimental in public docs until milestone criteria are met.

2. **Renderer primitives after preview reliability**
   - Full transform/crop/rotation.
   - Text rendering.
   - Blur.
   - Color correction.
   - Transitions.
   - PiP and mosaic helpers.
   - Reusable effect-chain intermediate targets.

3. **Media adapters after renderer primitives**
   - Desktop/window reliability.
   - Webcam.
   - Static image.
   - Animated image/GIF/APNG/WebP.
   - Lottie raster source.
   - Media file timeline/MP4.
   - RTSP/IP camera.
   - NDI input.

4. **Output adapters after source/runtime contracts**
   - Encoded file output.
   - RTMP/SRT streaming.
   - NDI output.
   - Virtual camera.

5. **Future audio track**
   - Audio source definitions.
   - Audio bus/mixer/clock contracts.
   - Mux sync metadata.
   - Capture/mix/mux/equalization only after the video pipeline is stable.


## Parallel UI Documentation and Mock Shell Track

A limited Avalonia Studio UI track may run in parallel with the active runtime roadmap when it stays strictly inside the UI/mock scope. This is allowed because it does not change engine contracts or open blocked media/runtime work.

Allowed UI work before runtime gates open:

- create/update `docs/UI_STUDIO_DESIGN.md`, `docs/UI_REACT_TO_AVALONIA_MAPPING.md`, `docs/UI_IMPLEMENTATION_PLAN.md`, and `docs/UI_ACCEPTANCE_CHECKLIST.md`;
- create an Avalonia dark-theme Studio shell;
- implement the Project Explorer, preview/canvas mock, Inspector, Bottom Workbench, Output Monitor, Diagnostics, Performance, Status Bar, and future Audio Mixer placeholder using mock data;
- implement fake command state for Start/Stop/Stream/Record;
- add ViewModel/unit tests for UI selection, command enablement, inspector routing, and mock state.

Still blocked until their roadmap step opens:

- real webcam, desktop/window, media file, animated image, Lottie, NDI, RTSP/IP camera, or stream source adapters;
- real encoded file, RTMP/SRT, NDI, or virtual-camera outputs;
- real audio capture, mixer, mux, or equalization;
- product preview integration beyond the approved `PreviewPanelSink` reliability work;
- any legacy direct capture/preview path.

The UI shell must be able to run without a GPU and without active MediaForge runtime integration during the first UI milestone.

## Blocking Rule

Do not implement the following until the roadmap step that owns it is active:

- productive preview shell beyond the `PreviewPanelSink` MVP wiring
- runtime-connected UI shells beyond the approved mock/design Avalonia Studio track
- NDI, RTSP/IP camera, webcam, MP4 decode, animated image, or Lottie runtime adapters
- encoder, recording, streaming, virtual camera, or audio sinks
- public plugin APIs

Allowed before those tracks open:

- documentation and tests
- API contract work
- render-graph planning
- package/preset serialization
- reliability fixes inside already-open runtime, renderer, source, and sink foundations

## Validation Gates

After each implementation unit:

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
```

When touching Capture, D3D11, Vulkan, GPU lifecycle, keyed mutex, registry,
render thread, provider, or submission, also run:

```powershell
./scripts/test.ps1 -Tier Gpu
```
