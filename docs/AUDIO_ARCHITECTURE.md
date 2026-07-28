# Audio Architecture

## Product boundary

Audio is a project-global graph. Scenes and video outputs select audio routes;
they never own or recreate physical audio devices.

```text
MediaForgeProject
  Video: sources, canvases, render outputs
  Audio: sources, nodes, connections, buses, routes, sinks
```

Audio source processing is performed once before fan-out. Route processing is
local to one route and bus processing is applied after mixing. A physical device
may be opened only once per logical source.

## Portable runtime contract

`WTK.MediaForge.Audio` is portable and references Core only. It owns the
serializable model, immutable compiled plans, buffer ownership, clocks and the
deterministic runtime. Native capture/playback belongs only in future
`WTK.MediaForge.Windows.Audio` and `WTK.MediaForge.Linux.Audio` adapters.

The internal format is planar float32, 48 kHz, mono or stereo. The logical
quantum defaults to 480 frames (10 ms); 240, 480 and 960 frames are supported.
The 120-frame/2.5 ms option is represented by configuration but is not required
from the first adapters. Native callback periods and graph quanta communicate
through bounded buffers, never per-block allocation.

Program Mix fan-out has one bounded pooled queue per route. Queue or pool
pressure drops only the affected route block and increments diagnostics; it
never blocks or faults the real-time callback or interrupts a healthy route.

Every `AudioBlockLease` carries format, frame count, monotonic timestamp,
duration, sequence number, discontinuity/silence flags, and explicit ownership.

## Real-time rules

The real-time callback does not block, await, acquire contended locks, access
disk, emit synchronous UI events, format logs, allocate, rebuild the graph, or
call a slow sink. It only processes a published immutable plan and bounded
buffers. Diagnostics and callbacks are emitted by non-real-time workers.

Graph updates are prepared off the callback thread, validated and compiled,
published between blocks, then retired after the final block lease releases.
A failed update keeps the prior plan active.

## Clocks and failure truth

Audio uses monotonic timestamps, an explicit master clock, drift estimation,
adaptive resampling contracts, latency accounting, underrun/overrun counters,
and an A/V PTS mapping contract. Device removal never selects another device
silently. Runtime states are `Running`, `Degraded`, `WaitingForDevice`,
`Failed`, and `Stopped`; fallback is possible only when explicitly configured.

## Deferred scope

The first cycle contains only generated tone, silence and test sources, memory
sinks, gain, mute, pan, polarity, channel mapping, mixing, meters and fixed
delay. Physical capture, loopback, application capture, virtual devices,
files, network/Remote Scene audio, mux/encode, EQ, dynamics, AEC, neural
processing, plugins, MIDI, surround, automation and encoded multitrack output
are unavailable until their own platform/runtime proofs exist.
