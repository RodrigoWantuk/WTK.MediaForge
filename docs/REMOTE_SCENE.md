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

## Engine media integration

The engine recognizes `remote-scene` as an encoded output route and includes it
in the existing render/encoder compatibility keys, so compatible MP4, RTMP, and
Remote Scene outputs may share rendered pixels and the hardware encoder while
retaining independent sink workers and failure state. The Remote Scene sink
accepts only `BackendOutputValidated` H.264 packets, transfers an explicit lease
to the publisher, and forwards keyframe feedback to the encoder boundary.

Receive-side contracts provide a bounded presentation-time jitter/reorder
buffer with keyframe-preserving drop behavior and deterministic lease cleanup.
`RemoteSceneHardwareDecodePump` recreates the hardware decoder on negotiated
format generation changes, rejects non-GPU decoder output, and yields owned GPU
frames. Its interruption policy explicitly selects last-frame freeze or a
placeholder; a host/provider owns the actual retained-frame presentation.
Telemetry carries RTT, loss, bitrate, jitter, selected candidate, frame,
keyframe, drop, relay, and reconnect values without media payloads.

Windows registers the source/output types but rejects activation with the
specific `remote-scene.publish`/`remote-scene.subscribe` capability reason. Even
test bypass flags cannot bypass this physical proof gate. This prevents the
contract-only native ABI from becoming a product route.

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

- an administrator bearer token scoped to one configured signaling instance,
  plus the matching `X-MediaForge-Instance` id, is required to create an invitation;
- invitation codes are one-time, expire by default after ten minutes, and are stored only as SHA-256 hashes;
- owner and participant access tokens are random 256-bit values and are also stored only as hashes;
- SQLite stores session coordination state with transactional redemption, WAL journaling, expiration cleanup, and no media payloads;
- each role has one active signaling connection per session; messages and outbound queues are bounded;
- TURN credentials use the time-limited REST/HMAC scheme supported by coturn;
- invitation creation, redemption, and WebSocket attachment are rate limited;
- HTTPS is mandatory except when an operator explicitly enables localhost-only development transport.
- operator/admin authorization is confined to invitation administration;
  one-time invite codes, client identity, per-session access tokens, and
  temporary TURN credentials are separate credential classes and are never
  logged;
- `X-Forwarded-For` and `X-Forwarded-Proto` are honored only from explicitly
  configured trusted proxy IPs, so externally terminated HTTPS and effective
  client-IP rate limits remain correct;
- the relay enforces publisher Offer, subscriber Answer, explicit
  renegotiation, role-correct messages, monotonic/idempotent sequences, bounded
  message rate, count and byte queues, and policy close reasons;
- process-wide, per-tenant, per-user, pending-invitation, WebSocket, creation
  rate, and TTL quotas are enforced. Structured logs and metrics contain only
  session correlation, role, kind, and rejection category—not tokens or SDP.

Quota counters are process-local. A multi-replica deployment must provide a
shared/distributed quota implementation or use sticky single-owner routing;
the checked-in in-memory tracker is not a cluster-wide limit.

The service refuses to start with the empty token in `appsettings.json`. Supply secrets through protected deployment configuration, for example:

```powershell
$env:RemoteSceneSignaling__AdminBearerToken = '<at-least-32-random-characters>'
$env:RemoteSceneSignaling__InstanceId = 'signaling-sa-east-1-a'
$env:RemoteSceneSignaling__TurnUrls__0 = 'turns://turn.example.com:5349'
$env:RemoteSceneSignaling__TurnSharedSecret = '<coturn-shared-secret>'
$env:RemoteSceneSignaling__TrustedProxies__0 = '10.0.0.5'
dotnet run --project WTK.MediaForge.Remote.Signaling
```

Production proxy, secret rotation, quota, health, SQLite, and coturn guidance is
documented in `docs/SIGNALING_DEPLOYMENT.md`.

The implemented signaling service does not by itself make Remote Scene media available. The pinned native libwebrtc bridge, hardware packet decoder integration, direct/TURN media proofs, and sustained recovery qualification remain required.

## Native Distribution

The C ABI is frozen at version 2 with opaque handles, versioned/sized structs,
typed error codes/messages, idempotent pointer-clearing destroy, explicit
borrowed callback buffers, and a no-callback-after-destroy guarantee. It covers
session SDP/ICE, ICE servers, connect/close, encoded H.264/optional audio,
packet/keyframe/state/candidate callbacks, selected candidate, and stats.

`WTK.MediaForge.Remote.WebRtc.Native/native-supply-chain.json` pins the official
libwebrtc LKGR source/tree and depot_tools revisions, toolchain, GN constraints,
wrapper hashes, artifacts, notices, platforms, and update process. The checked-in
contract build reports `mf_webrtc_backend_available() == 0`; it exists for ABI
testing only and CMake refuses that mode unless explicitly requested. It must
never be packaged as a supported remote-media feature. Only an adapter built
from the pin, with built-in software video codecs disabled, may report backend
availability, and it still requires Direct/TURN proof promotion.

## Physical Qualification Gate

`RemoteSceneQualificationGate` defines the evidence schema for Direct and TURN runs.
Each reviewed run records both adapters, the selected hardware encoder/decoder and ICE
candidate, TURN server (relay runs), RTT, loss, jitter, bitrate, frame/keyframe/reconnect
counters, RAM, VRAM, handles, queues, leases, deterministic shutdown, and baseline return.
The gate also requires 30-minute sustained runs and coverage of CGNAT, loss, bitrate and
keyframe changes, reconnect/abrupt shutdown, simultaneous MP4/RTMP, nested scenes, and
Apply/Live editing. Synthetic reports are not product proof and the evaluator does not
promote capabilities automatically.

No Direct or TURN physical report was produced in this repository. Both proof capabilities
therefore remain `Unavailable`.
