# Known Limitations

This file lists current product limitations. A limitation is not permission to add a fallback that violates the product contract.

## Platform availability

- Production video capture, hardware decode/encode, D3D11/Vulkan interop, MP4, RTMP, and current preview presentation are Windows-only.
- Linux and macOS projects contain portable/runtime boundaries and capability skeletons, not equivalent physical media products.
- Windows and Linux remain mandatory build/test targets for portable behavior.

## Hardware and proof dependence

- Preview, desktop capture, window capture, webcam capture, MP4 input, MP4 recording, and RTMP output require matching runtime capability and product proof on the active adapter/driver.
- A real implementation may still report `Unavailable` when its composite proof has not passed.
- Full Vulkan/D3D11 device-lost recreation and every sustained fault-injection scenario are not yet qualified end to end.
- The physical RenderGraph controls production plan validation, source acquisition imports, canvas/output execution, fan-out, and encoded dispatch, but remaining temporary/effect ownership must still be closed and sustained as the sole production authority.

## Studio

- Native hosted GPU preview remains proof-gated until attach, resize/DPI, rebind, detach, timeout, close, and sustained presentation pass.
- The Avalonia editor overlay is not capability evidence for native preview.
- Some source and output types are preserved canonically but do not yet have complete typed Studio editors.
- Studio persistence preserves fields covered by the canonical session mapper and round-trip tests. Unsupported editor surfaces must preserve opaque canonical data rather than silently rewriting it.

## Audio

- Portable audio graph compilation, pooled processing, mixing, meters, fixed delay, and bounded in-memory Program Mix routes are implemented.
- Physical audio capture, loopback, application capture, playback, encode, A/V mux, and Remote Scene audio are not implemented.
- Portable audio implementation must not be described as physical product availability.

## Remote Scene

- Signaling, invitations, authentication, SDP/ICE coordination, quotas, SQLite state, and coturn credential integration are implemented.
- Remote Scene media publish/subscribe remains unavailable.
- The checked-in native WebRTC target is contract-test-only and deliberately reports its backend unavailable.
- No functional pinned libwebrtc product binary or reviewed Direct/TURN physical evidence is distributed.

## Deferred or unavailable media

- Product NDI video is unavailable. Runtime detection and source discovery do not satisfy the GPU media path.
- SRT and virtual camera are unavailable.
- RTSP/IP-camera input, animated image formats, Lottie, advanced masks, and temporal effects remain planned or deferred according to the active roadmap.
- FFmpeg/libav, libx264, and libx265 are not product dependencies or fallbacks.

## Validation limitation

- Repository scripts and test infrastructure do not prove that a hardware-media feature passed on a specific machine unless the corresponding report was actually generated and reviewed.
- Hosted CI and portable tests cannot promote physical GPU, capture, codec, preview, or network-media capabilities by themselves.
