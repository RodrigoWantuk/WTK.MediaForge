# Pinned libwebrtc native build

The product ABI is version 2 and accepts encoded access units only. The checked-in
C++ file is the contract boundary and deliberately reports
`MF_WEBRTC_BACKEND_UNAVAILABLE` until the pinned libwebrtc adapter is linked.
That contract-only binary must not be packaged as a product runtime.

The authoritative pin, toolchain, GN arguments, wrapper hashes, artifact names,
platform scope, required notices, and update procedure are in
`native-supply-chain.json`. Fetch libwebrtc and depot_tools at exactly those
commits, run `gclient sync --revision src@<revision>`, and verify that the
checked-out Git tree equals the manifest before building.

The libwebrtc adapter must use PeerConnection encoded access-unit injection and
extraction. It must not instantiate a software video encoder/decoder or expose a
WebRTC `VideoFrame` path. Its build must keep
`rtc_include_builtin_video_codecs=false`. Any revision where these GN
constraints or the encoded API patch no longer apply fails the update review.

Before packaging:

1. run ABI layout/export tests on the produced DLL;
2. run repeated load/create/destroy and callback-in-flight shutdown tests;
3. run Offer/Answer and ICE loopback tests;
4. record SHA-256 hashes of the DLL/PDB in the release evidence;
5. copy upstream LICENSE, PATENTS, AUTHORS, README.chromium, and the transitive
   license scan into the distribution notice bundle;
6. run the Direct and TURN hardware proof gates.

No artifact is promoted merely because it loads or reports ABI version 2.
`mf_webrtc_backend_available()` must also return true and the composite proof
registry must pass.

