# Performance Baseline

Performance gates must execute real engine/runtime work. A timer loop or
`Task.Delay` scenario is not performance evidence.

## Automated Tier

```powershell
./scripts/test.ps1 -Tier Performance
```

The tier requires at least one Composition workload and one Vulkan workload:

- bounded completed-output fanout and lease release through real sink workers;
- repeated physical Vulkan multi-layer submissions with actual descriptor,
framebuffer, target-pool, fence, and cleanup accounting.

Readiness v14 additionally runs a 1080p60 real engine route through Vulkan,
D3D11/NV12, Media Foundation H.264, MP4, and a local TCP RTMP server. The
standalone sustained command is:

```powershell
./scripts/verify-sustained-media-runtime.ps1
./scripts/verify-sustained-media-runtime.ps1 -ReleaseCandidate
```

Test tiers are disjoint: Fast excludes GPU, Stress and Performance categories;
GPU excludes Stress and Performance; Performance runs only explicitly tagged
workloads. Fast includes non-GPU contracts from Core, Diagnostics, Composition,
Studio, D3D11, Vulkan, Capture and Windows projects.

No-test matches is a gate failure. Hardware absence must be represented by the
media proof report; it may not be hidden by an early-return test counted as pass.

## Qualification Workloads

The automated short tier is not release qualification. Local qualification must
run these real 1080p60 routes for 30 minutes, then eight hours for a release
candidate:

1. Vulkan scene -> preview;
2. Vulkan scene -> preview + MP4 + RTMP with one compatible encode group;
3. MP4 hardware decode -> GPU lease -> Vulkan output;
4. nested Live/Apply edit with output transition.

Capture CPU%, managed/native memory, handle count, source slots, Vulkan imports,
descriptor sets, framebuffer count, pooled targets, pending submissions, encode
latency, render latency, sink queue depth, drops, reconnects, and file samples.

## Acceptance

- Recording: zero dropped frames; overflow fails that recording route.
- Streaming: drops and reconnects are counted and do not block render/recording.
- No unbounded RAM/VRAM/handle/resource growth after warm-up.
- Every resource counter returns to baseline after stop.
- p95 latency and throughput are reported, not inferred from test duration.
- Results identify OS, adapter LUID, GPU name, driver, device generation, build,
  profile, resolution, FPS, and route configuration.
