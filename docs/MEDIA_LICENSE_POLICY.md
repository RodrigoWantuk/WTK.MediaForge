# Media License Policy

## General Rule

Only formats, SDKs, libraries, and codecs compatible with commercial product
distribution may appear as `Supported` or `Approved`.

## Prohibited by Default

- GPL/AGPL components in the distributed product binary
- `libx264` GPL without commercial license
- `libx265` GPL without commercial license
- FFmpeg builds with `--enable-gpl` or GPL components
- Strong copyleft components without compliance plan
- SDKs with unclear or non-redistributable commercial terms

## Allowed with Verification

- Operating system APIs (Media Foundation, VideoToolbox)
- GPU driver APIs with redistributable SDK and acceptable commercial terms
- MIT/BSD/Apache-2.0 libraries
- LGPL only when compliant: external process or dynamic linking, no GPL components, redistribution documented

## FFmpeg Policy

**FFmpeg is not used in the first hardware MP4/RTMP MVP.**

Future FFmpeg integration requires:

- LGPL-only build, no GPL components
- No libx264, libx265, rawvideo pipe
- License review before any product path

## Codec Policy

| Codec/Format | Status | Notes |
|--------------|--------|-------|
| H.264/AVC hardware encode | RequiresLegalReview | MF hardware MFT first on Windows |
| H.264/AVC hardware decode | RequiresLegalReview | Must produce GPU surface |
| HEVC/H.265 | Prohibited | Until legal approval |
| AV1 hardware | Planned | Hardware path only |
| AAC audio | Planned | Future; RequiresLegalReview |
| PCM/WAV | Planned | Future audio track |
| VP9 | Planned | Hardware path only |
| ProRes/DNxHR | Planned | Professional; license review |
| NDI Full/HX | Unsupported | SDK + license + GPU path |
| Virtual Camera | Unsupported | Platform path required |

## Static Image Decoders

| Format | Decoder | License | Status |
|--------|---------|---------|--------|
| PNG | `System.Drawing` / Windows built-in via approved path | OS/API | Approved for MVP |
| JPEG | `System.Drawing` / Windows built-in via approved path | OS/API | Approved for MVP |
| WebP | — | — | Planned until decoder license review |

Do not add arbitrary NuGet image decoders without updating this table first.

## Vendor SDK Direct Paths

| SDK | Status |
|-----|--------|
| NVENC direct | Planned, RequiresLegalReview |
| Intel QSV/oneVPL direct | Planned, RequiresLegalReview |
| AMD AMF direct | Planned, RequiresLegalReview |

Media Foundation hardware MFT is the primary Windows MVP encoder path.
