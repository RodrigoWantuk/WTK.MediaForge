# WTK MediaForge Architecture

## Overview

**WTK MediaForge** is a source-available, GPU-first audio and video composition engine focused on real-time media processing, hardware acceleration, and low system overhead.

The project is designed to compose media scenes using the GPU as the primary processing unit. The CPU should coordinate the pipeline, manage state, handle I/O, and synchronize resources, but it should not be responsible for processing full raw video frames whenever this can be avoided.

The initial target is a Windows desktop application using **.NET 8**, **WinForms**, **D3D11/DXGI**, **Vulkan**, **Silk.NET**, and **Vortice.Windows**. The long-term architecture should allow additional capture sources, render backends, encoders, streaming outputs, and platform-specific implementations.

The project aims to provide a lighter and more modular alternative to traditional live production tools, with a focus on:

- real-time audio/video composition;
- GPU-accelerated rendering;
- desktop, window, region, camera, stream, image, video, text, and audio sources;
- low CPU overhead;
- reduced movement of uncompressed video frames through system RAM;
- modular scene composition;
- nested canvases;
- preview/program workflows;
- hardware-accelerated encoding and decoding when available.

---

## Naming Guidelines

The public project name is:

```text
WTK MediaForge
```

Recommended public type prefix:

```text
MediaForge
```

Examples:

```text
MediaForgeCanvas
MediaForgeDrawObject
MediaForgeRenderer
MediaForgeProject
```

Short internal prefix, if needed:

```text
MF
```

Examples:

```text
MFFrame
MFTexture
MFRenderPass
```

Avoid using `WTKMF` in public APIs because it is visually heavy and harder to read. Avoid using `KMF` because it loses the direct connection to the WTK brand. Also avoid using `Canva`; the correct generic graphics term is `Canvas`.

---

## Core Design Idea

The central abstraction of WTK MediaForge is a **Canvas**.

A `MediaForgeCanvas` is a composition surface. It contains a list of drawable objects. These objects can be desktop captures, webcams, RTSP streams, video files, images, text, shapes, audio visualizers, or even other canvases.

This creates a recursive scene graph:

```text
Main Canvas
  â”œâ”€â”€ Desktop Capture
  â”œâ”€â”€ Text Overlay
  â”œâ”€â”€ Webcam
  â”œâ”€â”€ Secondary Canvas
  â”‚     â”œâ”€â”€ RTSP Stream
  â”‚     â”œâ”€â”€ Image Overlay
  â”‚     â””â”€â”€ Text
  â””â”€â”€ Another Canvas
```

A canvas can be rendered to:

- a preview panel;
- an offscreen GPU texture;
- another canvas as a nested object;
- an encoder input;
- a recording output;
- a streaming output.

This allows the engine to support:

- scene composition;
- nested scenes;
- picture-in-picture;
- mosaics;
- scene preloading;
- preview/program switching;
- transitions;
- render-to-texture workflows;
- multi-output rendering.

---

## High-Level Architecture

```text
WTK MediaForge Application
  â”‚
  â”œâ”€â”€ Project / Scene State
  â”‚     â”œâ”€â”€ Canvases
  â”‚     â”œâ”€â”€ Draw Objects
  â”‚     â”œâ”€â”€ Sources
  â”‚     â”œâ”€â”€ Effects
  â”‚     â””â”€â”€ Output Definitions
  â”‚
  â”œâ”€â”€ Capture Layer
  â”‚     â”œâ”€â”€ Desktop Duplication
  â”‚     â”œâ”€â”€ Windows Graphics Capture
  â”‚     â”œâ”€â”€ Webcam
  â”‚     â”œâ”€â”€ RTSP / Network Stream
  â”‚     â”œâ”€â”€ Video File
  â”‚     â””â”€â”€ Image / Text Sources
  â”‚
  â”œâ”€â”€ GPU Resource Layer
  â”‚     â”œâ”€â”€ D3D11 Textures
  â”‚     â”œâ”€â”€ Vulkan Images
  â”‚     â”œâ”€â”€ Shared Handles
  â”‚     â”œâ”€â”€ External Memory
  â”‚     â””â”€â”€ Synchronization Objects
  â”‚
  â”œâ”€â”€ Composition Layer
  â”‚     â”œâ”€â”€ Canvas Graph
  â”‚     â”œâ”€â”€ Draw Object Ordering
  â”‚     â”œâ”€â”€ Transforms
  â”‚     â”œâ”€â”€ Cropping
  â”‚     â”œâ”€â”€ Opacity
  â”‚     â”œâ”€â”€ Blend Modes
  â”‚     â””â”€â”€ Effects
  â”‚
  â”œâ”€â”€ Vulkan Renderer
  â”‚     â”œâ”€â”€ Swapchain Rendering
  â”‚     â”œâ”€â”€ Offscreen Rendering
  â”‚     â”œâ”€â”€ Shaders
  â”‚     â”œâ”€â”€ Pipelines
  â”‚     â”œâ”€â”€ Descriptor Sets
  â”‚     â””â”€â”€ Render Targets
  â”‚
  â”œâ”€â”€ Media Processing Layer
  â”‚     â”œâ”€â”€ FFmpeg Integration
  â”‚     â”œâ”€â”€ Hardware Decode
  â”‚     â”œâ”€â”€ Hardware Encode
  â”‚     â”œâ”€â”€ Audio Decode / Mix
  â”‚     â”œâ”€â”€ Muxing
  â”‚     â””â”€â”€ Streaming Protocols
  â”‚
  â””â”€â”€ Outputs
        â”œâ”€â”€ Preview
        â”œâ”€â”€ Program Output
        â”œâ”€â”€ Recording
        â”œâ”€â”€ RTMP / SRT / RTSP / HLS
        â””â”€â”€ Future Outputs
```

---

## Project Structure

Initial solution layout:

```text
WTK.MediaForge.sln

WTK.MediaForge.App.WinForms
WTK.MediaForge.Core
WTK.MediaForge.Capture
WTK.MediaForge.Graphics.D3D11
WTK.MediaForge.Graphics.Vulkan
WTK.MediaForge.Graphics.Interop
WTK.MediaForge.Composition
WTK.MediaForge.Diagnostics
```

Future projects may include:

```text
WTK.MediaForge.Media.FFmpeg
WTK.MediaForge.Media.Encoding
WTK.MediaForge.Media.Streaming
WTK.MediaForge.Audio
WTK.MediaForge.NativeBridge
WTK.MediaForge.Plugins
```

### Project Responsibilities

#### `WTK.MediaForge.Core`

Contains core models and contracts that should not depend on D3D11, Vulkan, WinForms, FFmpeg, or platform-specific APIs.

Examples:

```text
FrameSize
FrameRate
ColorRgba
Transform2D
CropRect
IRenderHost
ICaptureSource
IRenderer
MediaForgeProject
```

#### `WTK.MediaForge.Composition`

Contains the scene graph and composition model.

Examples:

```text
MediaForgeCanvas
MediaForgeDrawObject
DesktopCaptureDrawObject
WebcamDrawObject
RtspStreamDrawObject
ImageDrawObject
TextDrawObject
CanvasDrawObject
MediaForgeEffect
BlendMode
```

#### `WTK.MediaForge.Capture`

Contains capture source implementations.

Initial implementation:

```text
Desktop Duplication API
```

Future implementations:

```text
Windows Graphics Capture
Webcam Capture
RTSP Stream Input
Video File Input
Image Source
Text Source
```

#### `WTK.MediaForge.Graphics.D3D11`

Contains D3D11/DXGI-specific utilities and resource wrappers.

Examples:

```text
D3D11GpuDevice
D3D11TextureFrame
D3D11SharedTexture
D3D11AdapterInfo
```

#### `WTK.MediaForge.Graphics.Vulkan`

Contains the Vulkan renderer and Vulkan resource management.

Examples:

```text
VulkanPreviewRenderer
VulkanDevice
VulkanSwapchain
VulkanImage
VulkanPipeline
VulkanRenderTarget
```

#### `WTK.MediaForge.Graphics.Interop`

Contains interop logic between D3D11 and Vulkan.

Examples:

```text
D3D11ToVulkanInterop
ExternalMemoryHandle
ImportedD3D11Texture
KeyedMutexSynchronization
```

#### `WTK.MediaForge.Diagnostics`

Contains diagnostics and performance tools.

Examples:

```text
FpsCounter
FrameTiming
GpuDiagnostics
PipelineDiagnostics
```

---

## Canvas Model

A `MediaForgeCanvas` represents a renderable composition surface.

A canvas has:

- size;
- background;
- draw object list;
- optional output/render target;
- optional timing information;
- optional metadata.

Conceptual model:

```csharp
public sealed class MediaForgeCanvas
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public int Width { get; set; }

    public int Height { get; set; }

    public ColorRgba BackgroundColor { get; set; } = ColorRgba.Transparent;

    public List<MediaForgeDrawObject> Objects { get; } = new();
}
```

A canvas can be used as:

- the final program output;
- a preview scene;
- a nested scene;
- a picture-in-picture source;
- a reusable layout block;
- a render target for transitions or effects.

---

## Draw Objects

A `MediaForgeDrawObject` is anything that can be drawn onto a canvas.

Examples:

```text
DesktopCaptureDrawObject
WindowCaptureDrawObject
RegionCaptureDrawObject
WebcamDrawObject
RtspStreamDrawObject
VideoFileDrawObject
ImageDrawObject
TextDrawObject
CanvasDrawObject
ShapeDrawObject
AudioMeterDrawObject
```

Base conceptual model:

```csharp
public abstract class MediaForgeDrawObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public Transform2D Transform { get; set; } = Transform2D.Identity;

    public CropRect? Crop { get; set; }

    public float Opacity { get; set; } = 1.0f;

    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    public List<MediaForgeEffect> Effects { get; } = new();
}
```

Important distinction:

> Draw objects should describe what they are and how they should be composed.  
> They should not directly generate Vulkan shaders or manage Vulkan resources.

The renderer is responsible for translating draw objects into Vulkan commands, pipelines, descriptor sets, textures, and render passes.

---

## Canvas as a Draw Object

A canvas can be drawn inside another canvas by using a `CanvasDrawObject`.

Conceptual model:

```csharp
public sealed class CanvasDrawObject : MediaForgeDrawObject
{
    public required MediaForgeCanvas Canvas { get; init; }
}
```

This allows recursive composition:

```text
MainCanvas
  â”œâ”€â”€ DesktopCapture
  â”œâ”€â”€ Text
  â””â”€â”€ CanvasDrawObject -> SecondaryCanvas
        â”œâ”€â”€ Webcam
        â””â”€â”€ Image
```

Rendering strategy:

```text
1. Render SecondaryCanvas to an offscreen Vulkan texture.
2. Use that texture as a normal draw object inside MainCanvas.
3. Render MainCanvas to the preview/program/output target.
```

This enables:

- PiP;
- mosaics;
- reusable layouts;
- grouped effects;
- scene precomposition;
- transitions between canvases;
- preview/program switching.

---

## Source vs Draw Object

A **source** provides media frames, audio samples, images, or dynamic content.

A **draw object** places something onto a canvas.

Examples:

```text
DesktopCaptureSource
  provides GPU frames from a display

DesktopCaptureDrawObject
  references that source and defines where/how it appears on a canvas
```

This separation allows one source to be reused multiple times:

```text
One desktop capture source
  â”œâ”€â”€ full-screen object
  â”œâ”€â”€ cropped PiP object
  â””â”€â”€ magnified region object
```

It also allows source lifecycle to be independent from scene layout.

---

## Renderer Responsibilities

The renderer receives a canvas or a render snapshot and produces a GPU output.

The Vulkan renderer is responsible for:

- resolving canvas graph dependencies;
- rendering nested canvases to offscreen targets;
- ordering draw objects;
- binding resources;
- selecting pipelines;
- applying transforms;
- applying crop/scale/rotation;
- applying opacity and blend modes;
- applying effects;
- rendering text overlays;
- presenting to a swapchain or writing to an output texture.

The renderer should not own the editable project state directly. It should render a stable representation of the current scene.

---

## Scene Snapshots

The UI thread will edit canvases and draw objects in real time. The render thread should not directly iterate lists that the UI can modify concurrently.

Recommended strategy:

```text
Editable Scene Model
  â†“
Render Snapshot
  â†“
Vulkan Renderer
```

A render snapshot is a stable, frame-safe representation of the scene.

This prevents:

- collection modification during rendering;
- race conditions;
- resource lifetime issues;
- UI thread blocking;
- render thread instability.

Conceptually:

```text
UI Thread:
  modifies MediaForgeCanvas

Application Layer:
  generates RenderSceneSnapshot

Render Thread:
  renders RenderSceneSnapshot
```

---

## GPU-First Resource Strategy

The main performance goal is to avoid processing full raw video frames on the CPU.

Allowed/expected CPU/RAM usage:

```text
compressed packets
metadata
timestamps
scene state
text strings
small generated text/image resources
control messages
audio buffers
configuration
```

Avoid when possible:

```text
raw 1080p/4K frames as byte[]
Bitmap-based frame processing
CPU-based scaling
CPU-based chroma key
CPU-based composition
GPU -> RAM -> GPU roundtrips
rawvideo pipes for internal frame transfer
```

Preferred strategy:

```text
Capture / Decode
  -> GPU texture or hardware frame
  -> Vulkan image / sampled texture
  -> GPU composition
  -> GPU encoder or output render target
```

---

## Initial Windows Capture Pipeline

The initial POC uses the Windows Desktop Duplication API.

Current validated pipeline:

```text
Desktop Duplication API
  -> ID3D11Texture2D
  -> D3D11 shared NT handle
  -> Vulkan external memory
  -> Vulkan imported image
  -> Vulkan preview
```

Current components:

```text
D3D11 capture texture
D3D11 shared handle
D3D11 keyed mutex
Vulkan external memory
Vulkan dedicated allocation
Vulkan swapchain
WinForms preview panel
```

This validates the main GPU-first approach without converting the desktop frame into a `Bitmap` or `byte[]`.

---

## D3D11 and Vulkan Interop

On Windows, D3D11 is used as a practical video/capture interop layer.

Vulkan remains the main composition/rendering backend.

Interop strategy:

```text
D3D11 Texture
  -> IDXGIResource1::CreateSharedHandle
  -> Win32 shared NT handle
  -> Vulkan external memory import
  -> Vulkan image
```

Synchronization strategy:

```text
D3D11 keyed mutex:
  acquire key 0
  write/copy frame
  release key 1

Vulkan:
  acquire key 1
  read/sample/copy imported image
  release key 0
```

The first POC used `vkCmdCopyImage` to prove the D3D11 texture could reach Vulkan. This is useful as a proof of life, but it is not the final rendering strategy.

Final rendering strategy should use:

```text
Imported D3D11 image
  -> Vulkan ImageView
  -> Vulkan Sampler
  -> DescriptorSet
  -> Fragment Shader
  -> Canvas render target or swapchain
```

---

## Why Shaders Are Required

A direct image copy can prove that GPU interop is working, but it is not enough for a real compositor.

The renderer needs shaders for:

- scaling;
- cropping;
- rotation;
- aspect ratio handling;
- color conversion;
- alpha blending;
- chroma key;
- text rendering;
- picture-in-picture;
- mosaics;
- effects;
- transitions;
- nested canvas composition.

The correct long-term model is:

```text
Source texture
  -> sampled by shader
  -> transformed by draw object data
  -> blended into canvas render target
```

This is also necessary for correctly handling rotated displays. Desktop Duplication may provide the captured image in a non-rotated surface, while the logical monitor orientation may be portrait or landscape. Rotation should be handled in the shader or composition transform.

---

## Render Targets

A canvas may render to one of several targets:

```text
Swapchain target
  used for preview or UI display

Offscreen render target
  used for nested canvases, transitions, effects, or encoder input

Encoder render target
  used as input to hardware/software encoders

Recording render target
  used for file output

Streaming render target
  used for live streaming output
```

The main program output should not be tightly coupled to the preview window. The preview is just one consumer of a rendered canvas.

---

## Preview / Program Model

A future live production workflow may use:

```text
Preview Canvas
  scene being prepared

Program Canvas
  scene currently live/output
```

A simple switch can replace:

```text
ProgramCanvas = PreviewCanvas
```

A transition can blend two canvas render targets:

```text
ProgramCanvasTexture
PreviewCanvasTexture
TransitionProgress
  -> transition shader
  -> final output
```

This model supports OBS-like preview/program behavior while remaining canvas-based.

---

## Encoding and Decoding Strategy

WTK MediaForge should use FFmpeg for media I/O, demuxing, decoding, encoding, muxing, and streaming protocols, while keeping Vulkan responsible for visual composition.

Division of responsibilities:

```text
FFmpeg:
  protocols
  demux
  decode
  encode
  mux
  audio handling
  timestamps
  container formats

Vulkan:
  visual composition
  scaling
  crop
  effects
  overlays
  canvas rendering
  render targets

D3D11 / platform APIs:
  Windows capture interop
  hardware video surfaces
  GPU shared resources
```

### Initial FFmpeg Integration Modes

There are three possible integration levels.

#### Level 1: `ffmpeg.exe` process

Good for:

```text
simple conversion
MVP output
debugging commands
external encoding tests
basic streaming
```

Limitations:

```text
raw frame pipes involve system RAM
harder to access hardware frames directly
harder to integrate with Vulkan images
less control over timing and resources
```

#### Level 2: FFmpeg.AutoGen / libav* from .NET

Good for:

```text
direct API access
AVPacket / AVFrame control
hardware decode setup
hardware encoder setup
access to AVHWFramesContext
tighter integration
```

Challenges:

```text
unsafe code
native lifetime management
COM/D3D11 interop
AVBufferRef reference handling
complex error paths
```

#### Level 3: Native MediaBridge

A future production architecture may use a small native bridge library:

```text
WTK.MediaForge.NativeBridge
```

Responsible for:

```text
libavformat
libavcodec
AVHWDeviceContext
AVHWFramesContext
D3D11VA
NVENC / AMF / QSV
D3D11 textures
shared handles
encoder input
mux/output
```

The .NET application would call a simpler managed wrapper instead of directly managing all FFmpeg/D3D11/Vulkan native details in C#.

---

## Hardware Decode and Encode

The long-term goal is to use hardware acceleration whenever available.

### Windows Decode Options

Potential decode paths:

```text
D3D11VA
DXVA2
NVDEC / CUDA
Intel QSV
AMD AMF
Media Foundation
```

D3D11 is especially useful on Windows because it acts as a practical interop layer between capture, decode, hardware surfaces, and Vulkan external memory.

### Windows Encode Options

Potential encode paths:

```text
NVIDIA:
  h264_nvenc
  hevc_nvenc
  av1_nvenc

AMD:
  h264_amf
  hevc_amf
  av1_amf

Intel:
  h264_qsv
  hevc_qsv
  av1_qsv

Windows:
  Media Foundation encoders
```

The application should detect available encoders/decoders at startup and select the best compatible path.

---

## Codec Strategy

For maximum compatibility:

```text
MP4 + H.264 + AAC
```

This is the most widely supported output combination across devices, players, browsers, phones, TVs, and streaming platforms.

For a more open/modern output path:

```text
AV1 + Opus
```

Possible containers:

```text
WebM
MP4, depending on player/output target
```

### H.264 Without x264

The project should avoid depending on GPL-only FFmpeg builds when commercial dual licensing is a goal.

Avoid in LGPL-compatible FFmpeg builds:

```text
libx264
libx265
--enable-gpl
--enable-nonfree
```

Prefer hardware encoders where possible:

```text
h264_nvenc
h264_amf
h264_qsv
h264_mf
h264_vaapi
h264_videotoolbox
```

The output is still H.264, but it is not produced by the GPL `libx264` encoder.

### AAC

For simple compatibility:

```text
-c:a aac
```

Use FFmpeg's native AAC encoder where possible.

For a more open audio path:

```text
Opus
```

### AV1

AV1 is a strong long-term codec option because it is modern and efficient. However, it is not yet the universal compatibility choice for all older devices, smart TVs, browsers, and hardware decoders.

Recommended approach:

```text
Default compatibility profile:
  H.264 + AAC + MP4

Modern/open profile:
  AV1 + Opus

Professional/advanced profile:
  user-selectable encoders based on hardware
```

---

## FFmpeg Licensing Strategy

The project should keep FFmpeg integration compatible with the project's source-available/commercial licensing goals.

Recommended FFmpeg strategy:

```text
Use LGPL-compatible FFmpeg builds.
Do not enable GPL components.
Do not enable nonfree components.
Avoid libx264 and libx265 unless using a separate licensing/compliance strategy.
Prefer dynamic linking or external process usage.
Maintain third-party notices.
Allow replacement of LGPL DLLs when distributed.
```

If FFmpeg binaries are distributed with the application, the project must document:

```text
FFmpeg version
configure options
license
source availability
third-party notices
any patches or modifications
```

The project license applies to WTK MediaForge code. Third-party components keep their own licenses.

---

## License Model

WTK MediaForge is intended to be source-available under:

```text
PolyForm Noncommercial License 1.0.0
```

Commercial use requires a separate written commercial license from the author.

This model allows:

```text
personal use
study
experimentation
research
hobby projects
evaluation
non-commercial modification
```

Commercial, industrial, SaaS, broadcast, resale, consulting, integration into paid products or services, production use, or revenue-generating use requires a separate license.

The project should be described as:

```text
source-available
```

not as traditional open source, because traditional open source licenses do not restrict commercial use.

---

## Threading Model

Recommended runtime model:

```text
UI Thread:
  user interaction
  project editing
  canvas editing
  source selection

Capture Threads:
  desktop capture
  webcam capture
  stream receiving
  video decoding

Render Thread:
  snapshot rendering
  Vulkan command recording
  GPU synchronization
  preview/program output

Encoding Thread:
  receives final output frames
  feeds hardware/software encoder
  muxes or streams output

Audio Thread:
  captures audio
  mixes audio
  synchronizes audio/video
```

The render thread should not block on UI editing. Capture sources should provide the latest available GPU frame to the renderer.

---

## Resource Lifetime Rules

Important rules:

```text
Do not destroy a GPU resource while it may still be used by the renderer.
Do not let UI-owned scene objects directly own Vulkan objects.
Use snapshots or resource references for rendering.
Use explicit Dispose patterns for native resources.
Keep D3D11 and Vulkan resources in backend-specific projects.
Keep Core and Composition free from Vulkan/D3D11 dependencies.
```

Recommended separation:

```text
Composition object:
  describes desired content

Runtime source:
  provides current frame/resource

Vulkan resource:
  backend-specific representation

Renderer:
  binds and draws resources
```

---

## Current POC Status

Validated:

```text
.NET 8 WinForms host
Desktop monitor enumeration
Desktop Duplication API capture
D3D11 texture capture
D3D11 GPU-to-GPU CopyResource
D3D11 shared NT handle creation
D3D11 keyed mutex usage
Vulkan instance creation
WinForms Panel to Vulkan surface
Vulkan swapchain presentation
Vulkan external memory import
D3D11 texture reaching Vulkan
```

Current temporary renderer path:

```text
D3D11 imported image
  -> vkCmdCopyImage
  -> swapchain
```

Known limitations:

```text
direct copy does not scale
direct copy does not rotate
direct copy does not preserve aspect ratio intentionally
direct copy is not the final composition path
portrait monitor handling requires rotation logic
visual composition requires shader-based rendering
```

Next architectural step:

```text
D3D11 imported image
  -> Vulkan ImageView
  -> Vulkan Sampler
  -> DescriptorSet
  -> Fragment Shader
  -> Canvas rendering pipeline
```

---

## Immediate Next Technical Milestones

### Milestone 1: Formal Composition Model

Create the initial high-level model:

```text
MediaForgeCanvas
MediaForgeDrawObject
DesktopCaptureDrawObject
TextDrawObject
CanvasDrawObject
Transform2D
CropRect
BlendMode
```

### Milestone 2: Renderer Snapshot

Create a simple render snapshot model so the renderer does not directly consume editable UI state.

```text
MediaForgeCanvas
  -> RenderCanvasSnapshot
  -> VulkanRenderer
```

### Milestone 3: Shader-Based Texture Rendering

Replace direct `vkCmdCopyImage` preview with:

```text
Imported Vulkan Image
  -> ImageView
  -> Sampler
  -> Fullscreen Triangle
  -> Fragment Shader
```

### Milestone 4: Fit / Fill / Crop / Rotation

Add draw object layout behavior:

```text
Fit
Fill
Stretch
Crop
Manual transform
Rotation
```

### Milestone 5: Text Overlay

Implement text as a draw object.

Initial approach:

```text
Text string
  -> rasterized texture when text changes
  -> Vulkan sampled texture
  -> draw on canvas
```

Future approach may use:

```text
glyph atlas
SDF/MSDF text rendering
GPU text rendering
```

### Milestone 6: Canvas-to-Canvas Rendering

Render a canvas into an offscreen Vulkan render target, then use that texture as a draw object inside another canvas.

---

## Long-Term Direction

WTK MediaForge should evolve toward a modular media composition engine where the application UI is only one shell over the core runtime.

Possible future features:

```text
desktop capture
window capture
region capture
webcam capture
RTSP input
video file playback
image overlays
text overlays
audio capture
audio mixing
audio meters
PiP
mosaic layouts
scene transitions
nested canvases
preview/program workflow
recording
streaming
hardware encoding
plugin architecture
remote control API
web control panel
```

The core design principle remains:

> WTK MediaForge should describe media composition at a high level, but execute it through a GPU-first rendering pipeline.