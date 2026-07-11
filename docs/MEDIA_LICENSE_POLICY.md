# Media License Policy

## General Rule

Only formats, SDKs, libraries, and codecs compatible with commercial product
distribution may appear as `Supported` or `Approved`.

Continuous video decode and encode must use platform hardware acceleration.
If a GPU/driver/OS backend cannot provide a validated GPU surface path, the
feature must report unavailable instead of falling back to software decode,
software encode, CPU staging, or raw video pipes.

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

**FFmpeg is not used in the first hardware MP4/RTMP product path.**

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
| PNG | `System.Drawing` / Windows built-in via approved path | OS/API | Approved for product path |
| JPEG | `System.Drawing` / Windows built-in via approved path | OS/API | Approved for product path |
| WebP | N/A | N/A | Planned until decoder license review |

Do not add arbitrary NuGet image decoders without updating this table first.

## Vendor SDK Direct Paths

| SDK | Status |
|-----|--------|
| NVENC direct | Planned, RequiresLegalReview |
| Intel QSV/oneVPL direct | Planned, RequiresLegalReview |
| AMD AMF direct | Planned, RequiresLegalReview |

Media Foundation hardware MFT is the primary Windows hardware encoder path.

## FFmpeg Libraries Integration Review

FFmpeg is not part of the first MP4/RTMP hardware product path.

The engine may evaluate FFmpeg libraries in a future phase only for encoded-packet/container-level work, never as a raw video frame processing path.

### Allowed future uses, pending license review

The following uses may be evaluated in a future dedicated integration phase:

- `libavformat` for demuxing encoded packets from containers or streams;
- `libavformat` for muxing already encoded packets into supported containers;
- `libavutil` for auxiliary packet/container utilities;
- `libavcodec` only for parsing, bitstream filtering, or codec metadata when it does not perform continuous software video decode/encode.

All future FFmpeg usage must satisfy all of these conditions:

- LGPL-only build;
- no GPL components;
- no `--enable-gpl`;
- no `--enable-nonfree`;
- dynamic linking unless legal review approves otherwise;
- documented build configuration;
- third-party notices and compliance material updated;
- no raw video pipe;
- no software video encode/decode in the product path;
- no decompressed video frame crossing CPU/RAM in normal runtime.

### Explicitly prohibited

The following are prohibited in product code:

- `ffmpeg.exe` process execution for product encode/decode;
- rawvideo pipe into FFmpeg;
- `libx264`;
- `libx265`;
- GPL FFmpeg builds;
- nonfree FFmpeg builds;
- software video encode as fallback;
- software video decode as fallback for continuous video sources;
- `AVFrame`/raw YUV/RGBA frame flowing through CPU/RAM as part of normal product runtime.

### Relationship with the GPU Media Law

FFmpeg libraries, if ever accepted, may only operate on encoded packets, containers, metadata, or codec configuration data.

Allowed future shape:

```text
Container / network stream
  -> FFmpeg demux, encoded packets only
  -> EncodedVideoPacket
  -> hardware decoder
  -> GPU frame
```

Prohibited shape:

```text
Container / network stream
  -> FFmpeg software decode
  -> raw AVFrame in RAM
  -> upload to GPU
```
