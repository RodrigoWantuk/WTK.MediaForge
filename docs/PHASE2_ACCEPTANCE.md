# Phase 2 Acceptance

Evidence for WTK MediaForge Phase 2 (GPU Pipeline Completo) commits 01-20.

## Validation commands

```powershell
git diff --stat
dotnet test
./scripts/test.ps1 -Tier Fast
./scripts/test.ps1 -Tier Gpu
./scripts/verify-phase2-readiness.ps1
./scripts/verify-engine-readiness-v6.ps1
```

## Commit gates

| # | Commit | Evidence |
|---|--------|----------|
| 01 | GPU Resource Lifetime | `WTK.MediaForge.Core.Tests/Gpu/GpuResourcePoolTests.cs` |
| 02 | GPU Frame Scheduler | `WTK.MediaForge.Composition.Tests/Scheduling/FrameSchedulerTests.cs` |
| 03 | Asset Manager | `WTK.MediaForge.Composition.Tests/Assets/AssetManagerTests.cs` |
| 04 | GPU Surface Export Proof | `WTK.MediaForge.Windows.Tests/Media/WindowsGpuExportProofTests.cs` |
| 05 | Hardware Decode Foundation | `WTK.MediaForge.Core.Tests/Media/DecodedGpuFrameTests.cs` |
| 06 | Windows Hardware Decode Prototype | Windows hardware decode prototype tests in `WTK.MediaForge.Windows.Tests/Media` |
| 07 | Video Source Runtime | `WTK.MediaForge.Composition.Tests/Sources/VideoSourceRuntimeTests.cs` |
| 08 | Texture Streaming | `WTK.MediaForge.Composition.Tests/Streaming/TextureLeaseQueueTests.cs` |
| 09 | Renderer Video Integration | `WTK.MediaForge.Graphics.Vulkan.Tests/VideoPreviewIntegrationTests.cs` |
| 10 | Scene Runtime | `WTK.MediaForge.Composition.Tests/Scene/SceneRuntimeTests.cs` |
| 11 | Render Graph Executor | `WTK.MediaForge.Composition.Tests/Rendering/RenderGraphExecutorTests.cs` |
| 12 | GPU Effects Framework | `WTK.MediaForge.Graphics.Vulkan.Tests/Effects/EffectGraphFrameworkTests.cs` |
| 13 | Transform Effects | `WTK.MediaForge.Graphics.Vulkan.Tests/Effects/TransformEffectTests.cs` |
| 14 | Text Rendering | `WTK.MediaForge.Graphics.Vulkan.Tests/Text/TextRenderingTests.cs` |
| 15 | Hardware Encode Foundation | `WTK.MediaForge.Windows.Tests/Media/HardwareEncodeFoundationTests.cs` |
| 16 | MP4 Recording Packet Boundary | `WTK.MediaForge.Composition.Tests/Media/EncodedOutputPipelineTests.cs` |
| 17 | RTMP Output Prototype | `EncodedOutputPipelineTests.Rtmp_sink_receives_flv_tags_from_shared_encoder` |
| 18 | Synthetic Performance Validation | `WTK.MediaForge.Diagnostics.Tests/Performance/PerformanceValidationSuiteTests.cs` and `WTK.MediaForge.Composition.Tests/Performance/CompositionPerformanceGateTests.cs` |
| 19 | Fault Recovery | `WTK.MediaForge.Composition.Tests/Recovery/FaultRecoveryCoordinatorTests.cs` |
| 20 | Engine Readiness Gate | `./scripts/verify-phase2-readiness.ps1` and `./scripts/verify-engine-readiness-v6.ps1` |

## Blocking rules verified

- No FFmpeg/libx264 in MP4/RTMP runtime paths (`verify-license-policy.ps1`)
- No CPU continuous video transport violations (`verify-media-transport-rules.ps1`)
- Sinks do not invoke render (`FrameSchedulerGuardRailTests`)
- Export proof audit events present for encode path

## Performance artifacts

See `docs/PERFORMANCE_BASELINE.md` and `artifacts/performance/`.

## Status

Phase 2 structural contracts are present, but media paths that depend on
hardware encode/decode, MP4 recording, RTMP streaming, and performance
validation remain prototype infrastructure until real backend proof replaces
canned packets, placeholder textures, in-memory transports, and synthetic
workloads. These rows must not be read as product-ready decode, recording, or
streaming support.

## Product Readiness Labels

Phase 2 acceptance evidence is now classified separately from support status:

| Label | Meaning |
|---|---|
| Done:Contract | API, lifetime, scheduler, or validation contract exists. |
| Done:Skeleton | Structural placeholder exists, but no real product behavior yet. |
| Done:Prototype | Internal prototype exists and must remain blocked from product capability. |
| Done:BackendCallSucceeded | A backend interop/proof call succeeded, but full product output is not validated. |
| Done:ProductValidated | Current scoped behavior is product-level and covered by tests. |

Current interpretation:

- GPU resource lifetime, scheduler, asset manager, source runtime, scene runtime,
  texture streaming, and fault recovery are `Done:Contract`.
- GPU surface export proof is `Done:BackendCallSucceeded`.
- Windows hardware decode, Windows hardware encode, MP4 recording end-to-end,
  and RTMP output are `Done:Prototype`.
- The MP4 packet writer boundary is `Done:Contract`: the public recording sink
  now requires trusted `BackendOutputValidated` H.264 packet evidence and
  rejects prototype or contract-only packets, but product recording remains
  unavailable until real hardware encoder output is validated.
- RenderGraph executor has a resource bridge but is not a GPU pass executor yet.
- `ColorCorrectionEffect` is product-validated in the Vulkan source-layer
  shader, and `BlurEffect` is product-validated for the current Vulkan
  source-layer intermediate-pass scope.
- Text rendering is `Done:ProductValidated` for Windows Vulkan glyph atlas text
  layers.
- Output route cut/fade transitions are `Done:ProductValidated` for the current
  Vulkan output pass scope.
- Synthetic performance validation is `Done:Skeleton`.
