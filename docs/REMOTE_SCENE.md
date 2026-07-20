# Remote Scene Link

Remote Scene is a hardware-first video link between two MediaForge instances:

`GPU surface -> existing hardware H.264 encoder -> WebRTC/SRTP -> WebRTC -> existing hardware decoder -> GPU surface`

V1 is opaque H.264 video only. Audio, alpha, scene-graph synchronization, remote editing, simulcast, and asset transfer are unsupported.

## Boundaries

- `WTK.MediaForge.Remote` owns platform-neutral connection, publish, subscribe, state, telemetry, and signaling/ICE contracts.
- `WTK.MediaForge.Remote.WebRtc` owns the managed P/Invoke ABI boundary.
- `WTK.MediaForge.Remote.WebRtc.Native` is the reproducible CMake wrapper boundary for a pinned libwebrtc revision.
- Windows will connect packet ingress/egress to the existing Media Foundation hardware encoder/decoder only after the native encoded-access-unit bridge is built and qualified.

The native bridge must inject H.264 after MediaForge encoding and surface received access units before MediaForge decoding. It must not create or accept WebRTC `VideoFrame` CPU paths, software codecs, or continuous uncompressed CPU frames.

## Capability Truth

The presence of the managed contracts does not promote Remote Scene. Publish and subscribe remain unavailable until all of the following pass on the active adapter:

1. pinned native libwebrtc bridge ABI is installed;
2. hardware H.264 encoder and decoder are available on the same GPU identity;
3. direct and TURN packet proofs preserve SRTP, ordering, keyframe request, and reconnection;
4. end-to-end GPU decode-to-render proof has no continuous readback.

`WebRtcConnectionOptions` is runtime-only. Project serialization stores a connection profile reference, never signaling bearer tokens, TURN credentials, invitation codes, or SDP/ICE material.

## Native Distribution

Before shipping the native bridge, pin the libwebrtc revision and include its BSD-3-Clause license, `PATENTS`, `AUTHORS`, and all transitive native dependency notices in `THIRD_PARTY_NOTICES.md`. A standalone ABI stub is not a WebRTC implementation and must not be packaged as a supported remote-media feature.
