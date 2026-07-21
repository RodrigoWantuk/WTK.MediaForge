# Known Limitations

- Production media adapters are currently Windows-only. Linux and macOS projects
  contain platform boundaries, not equivalent capture/decode/encode products.
- MP4, RTMP, preview, webcam, desktop/window capture, and file decode are
  hardware/proof dependent and may report `Unavailable` on a machine where the
  required composite evidence has not passed.
- Remote Scene signaling is implemented, but Remote Scene media is unavailable.
  The checked-in native ABI target is contract-test-only and reports its backend
  unavailable; no functional libwebrtc product binary or Direct/TURN physical
  evidence is distributed.
- Audio capture, mixing, encoding, muxing, and Remote Scene audio are not implemented.
- SRT, virtual camera, and product NDI video are unavailable.
- FFmpeg/libav, libx264, and libx265 are not product dependencies or fallbacks.
- Studio project persistence preserves canonical data covered by its round-trip
  tests; fields outside the implemented mapper contract must not be described as
  universally round-trippable.
- Studio native GPU preview remains gated; the editor overlay is not proof of a
  promoted preview path.
