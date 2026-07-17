# Media License Policy

## General Rule

Only formats, SDKs, libraries, and codecs compatible with commercial product
distribution may appear as `Supported` or `Approved`.

Continuous video decode and encode must use platform hardware acceleration.
If a GPU/driver/OS backend cannot provide a validated GPU surface path, the
feature must report unavailable instead of falling back to software decode,
software encode, CPU staging, or raw video pipes.
Even when a codec/backend is legally acceptable, it is not product-supported
until the matching v12 `HardwareMediaProof` entries pass with
`BackendOutputValidated` evidence. Codec proofs do not automatically promote
real media I/O: MP4 input/output, webcam input, RTMP network output, and NDI
input/output each require their own product proof.

## NDI Policy

NDI SDK availability is not the same as product readiness. The Standard NDI SDK
is publicly downloadable and royalty-free subject to Vizrt NDI's SDK license
terms, and its redistributable/runtime may be distributed only when the
application satisfies the SDK/EULA, attribution, and runtime distribution
requirements. It is not an open-source/libre dependency.

MediaForge may dynamically detect an installed NDI runtime (`NDI_RUNTIME_DIR_V6`,
`NDI_RUNTIME_DIR_V5`, application directory, NuGet native assets, `Program Files`
runtime folders, or `PATH`). Release builds may redistribute the Standard SDK
runtime DLLs by placing licensed copies under `third_party/ndi/windows/*`; the
Windows project will copy and pack them as `runtimes/win-*/native` assets when
present. Detection and redistributable packaging only prove the runtime is
present and loadable. They do **not** promote NDI video input/output to product
support.

NDI product support requires all of the following:

- Standard SDK redistribution terms satisfied for the shipped package;
- trademark/attribution/EULA coverage in the host application;
- a GPU-safe input path that produces GPU-importable source leases or encoded
  transport without continuous raw CPU video frames;
- a GPU-safe output path that sends rendered GPU surfaces or hardware encoded
  packets without continuous CPU readback;
- `proof.media_io.ndi_input.product` and/or
  `proof.media_io.ndi_output.product` passing with
  `BackendOutputValidated` evidence.

The Standard SDK is accepted for runtime detection, redistribution, and source
discovery. Its frame-buffer send/receive path is not accepted as a product path
for continuous video because it would move decompressed video through CPU/RAM.
Any future NDI Advanced or vendor-specific path must remain isolated in the
platform adapter and pass the same GPU Media Law.

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

**FFmpeg is deferred until the native hardware MP4/RTMP product path is sustained.**

Future FFmpeg integration requires:

- LGPL-only build, no GPL components
- No libx264, libx265, rawvideo pipe
- Encoded-packet/container-only scope
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
| NDI Full/HX | Discovery supported / video blocked | Standard SDK runtime can be detected, redistributed, and used for source discovery; product video support requires GPU-safe input/output proofs |
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
