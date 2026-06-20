# Current Roadmap — GPU Lifecycle + Product Model

This roadmap is mandatory. Do not choose a different order within each track.

## Status

- **P0 GPU lifecycle (commits 1–11):** complete
- **Product model formalization (H1–H7):** H1 complete; H2–H7 pending
- **Visual compositing + real sources/outputs:** blocked until H7

## Blocking rule (product features)

Until product commits H2–H7 are complete, do not implement:

- UI shells beyond test harnesses
- NDI, RTSP, webcam, MP4 decode sources
- encoder, audio, streaming sinks
- preview binding in production app
- ad hoc draw object types per media format

Documentation and contract work (H1) is allowed and required.

## Completed — P0 GPU lifecycle

1. Provider lifecycle gate + DisposeFailed
2. Ring FullyDisposed faulted
3. Dedupe by VulkanExternalTextureKey
4. ArrayPool + limit 128 imports
5. Remove IAsyncDisposable from submissions
6. Remove synchronous WaitIdle from IRenderBackend
7. MediaForgeVulkanRenderer internal + public factory
8. IVulkanRendererFaultInjector (no Simulate*)
9. Registry acquire outside global lock
10. ARCHITECTURE.md final contracts
11. Offscreen render target scaffolding

## Next — Product model (H1–H7)

See [PRODUCT_MODEL.md](PRODUCT_MODEL.md) for full contract.

| # | Commit |
|---|--------|
| H1 | Product model documentation |
| H2 | Source type catalog + typed settings |
| H3 | Output type catalog + typed settings |
| H4 | Effect model |
| H5 | MediaForgeProjectEditor |
| H6 | Advanced graph validation (cycles, depth 8) |
| H7 | MediaForgeEngine facade skeleton |

## After H7

1. Minimal compositing: source layer → offscreen target (fit)
2. Real sources: desktop (exists), webcam, NDI, RTSP, video file
3. Real outputs: preview window, offscreen, NDI, MP4, streaming

## Validation gates

After each code commit:

```powershell
dotnet test
./scripts/test.ps1 -Tier Fast
./scripts/test.ps1 -Tier Gpu   # when touching GPU/Capture/Vulkan
```
