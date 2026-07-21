# Remote Scene Link

Remote Scene is a hardware-first video link between two MediaForge instances:

`GPU surface -> existing hardware H.264 encoder -> WebRTC/SRTP -> WebRTC -> existing hardware decoder -> GPU surface`

V1 is opaque H.264 video only. Audio, alpha, scene-graph synchronization, remote editing, simulcast, and asset transfer are unsupported.

## Boundaries

- `WTK.MediaForge.Remote` owns platform-neutral connection, publish, subscribe, state, telemetry, and signaling/ICE contracts.
- `WTK.MediaForge.Remote.WebRtc` owns the managed P/Invoke ABI boundary.
- `WTK.MediaForge.Remote.WebRtc.Native` is the reproducible CMake wrapper boundary for a pinned libwebrtc revision.
- `WTK.MediaForge.Remote.Signaling` is the separately deployed HTTPS/WebSocket coordination service. It authenticates peers and relays SDP/ICE messages only; it never transports media.
- Windows will connect packet ingress/egress to the existing Media Foundation hardware encoder/decoder only after the native encoded-access-unit bridge is built and qualified.

The native bridge must inject H.264 after MediaForge encoding and surface received access units before MediaForge decoding. It must not create or accept WebRTC `VideoFrame` CPU paths, software codecs, or continuous uncompressed CPU frames.

## Capability Truth

The presence of the managed contracts does not promote Remote Scene. Publish and subscribe remain unavailable until all of the following pass on the active adapter:

1. pinned native libwebrtc bridge ABI is installed;
2. hardware H.264 encoder and decoder are available on the same GPU identity;
3. direct and TURN packet proofs preserve SRTP, ordering, keyframe request, and reconnection;
4. end-to-end GPU decode-to-render proof has no continuous readback.

Remote Scene is a canonical `remote-scene` output/source type. Project JSON may
store provider, signaling endpoint, stream/session policy, codec preferences,
resolution/video profile, and reconnection policy. `WebRtcConnectionOptions`
and `RemoteSceneRuntimeCredentials` are runtime-only; bearer/session tokens,
TURN usernames/credentials, invitation codes, and SDP/ICE material are never
project settings.

Encoded packet ownership always crosses the API as an
`EncodedVideoPacketLease`. Sending transfers the lease to the publisher until
native completion or rejection; receiving yields owned leases through an
asynchronous stream and requires consumer disposal. Both directions declare a
positive bounded capacity, operation timeout, and slow-consumer policy. Video
defaults to dropping delta frames until the next keyframe; it never grows an
unbounded queue. RTCP PLI/FIR is surfaced to the publisher through
`KeyFrameRequested`, which is the hardware encoder's signal to emit an IDR.

## Signaling Service

The signaling service is implemented with the following product boundaries:

- an administrator bearer token is required to create an invitation;
- invitation codes are one-time, expire by default after ten minutes, and are stored only as SHA-256 hashes;
- owner and participant access tokens are random 256-bit values and are also stored only as hashes;
- SQLite stores session coordination state with transactional redemption, WAL journaling, expiration cleanup, and no media payloads;
- each role has one active signaling connection per session; messages and outbound queues are bounded;
- TURN credentials use the time-limited REST/HMAC scheme supported by coturn;
- invitation creation, redemption, and WebSocket attachment are rate limited;
- HTTPS is mandatory except when an operator explicitly enables localhost-only development transport.

The service refuses to start with the empty token in `appsettings.json`. Supply secrets through protected deployment configuration, for example:

```powershell
$env:RemoteSceneSignaling__AdminBearerToken = '<at-least-32-random-characters>'
$env:RemoteSceneSignaling__TurnUrls__0 = 'turns://turn.example.com:5349'
$env:RemoteSceneSignaling__TurnSharedSecret = '<coturn-shared-secret>'
dotnet run --project WTK.MediaForge.Remote.Signaling
```

The implemented signaling service does not by itself make Remote Scene media available. The pinned native libwebrtc bridge, hardware packet decoder integration, direct/TURN media proofs, and sustained recovery qualification remain required.

## Native Distribution

Before shipping the native bridge, pin the libwebrtc revision and include its BSD-3-Clause license, `PATENTS`, `AUTHORS`, and all transitive native dependency notices in `THIRD_PARTY_NOTICES.md`. A standalone ABI stub is not a WebRTC implementation and must not be packaged as a supported remote-media feature.
