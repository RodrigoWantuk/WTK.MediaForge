# Remote Scene Signaling and TURN Deployment

The signaling service coordinates invitations, authentication, SDP, and ICE.
It never carries media. coturn relays encrypted WebRTC packets when a direct ICE
route is unavailable; coturn is not the signaling service and does not decode,
encode, or compose video.

Deploy `WTK.MediaForge.Remote.Signaling` behind HTTPS/WSS with persistent SQLite
storage, a random administrator bearer token, explicit trusted proxy addresses,
bounded quotas, and protected health credentials. Supply secrets through the
deployment secret store, never `appsettings.json` or project files.

Required production configuration includes:

- `AdminBearerToken` of at least 32 random characters;
- `TrustedProxies` for every proxy allowed to set forwarded headers;
- a persistent, backed-up `DatabasePath` with restricted filesystem access;
- `TurnUrls` using `turns:` in production and a coturn REST/HMAC shared secret;
- invitation/session/WebSocket/rate/byte limits sized for the deployment;
- log retention that excludes tokens, invitation codes, credentials, SDP, and ICE payloads.

Rotate the signaling admin token and TURN shared secret independently. Revoked
session tokens remain rejected by the service. Health, quota, policy-close, and
rejection metrics should be monitored without payload logging.

A healthy signaling deployment and a reachable TURN server do not make Remote
Scene available. Product availability still requires the pinned functional
libwebrtc adapter, hardware H.264 packet ingress/egress, GPU decode surfaces,
Direct and TURN physical proofs, reconnect qualification, and sustained baseline return.
