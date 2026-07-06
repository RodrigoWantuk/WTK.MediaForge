# Performance Baseline

Phase 2 Commit 18 performance thresholds and validation contract.

## Suite

- Project: `WTK.MediaForge.Diagnostics.Tests`
- Class: `PerformanceValidationSuite`
- Artifacts: `artifacts/performance/performance_*.json` and `performance_*.md`

## Scenario duration

| Build | Duration per scenario |
|-------|----------------------|
| DEBUG | 2 seconds (`PerformanceValidationSuite.DebugScenarioSeconds`) |
| RELEASE | 300 seconds (`PerformanceValidationSuite.ReleaseScenarioSeconds`) |

## Scenarios

1. `video_playback`
2. `composition_stress`
3. `recording_path`
4. `streaming_path`

## Thresholds (initial baseline)

| Metric | Threshold |
|--------|-----------|
| Dropped frames | <= 25% of total frames in scenario |
| Average frame time | informational only in DEBUG |
| CPU percent | informational only in DEBUG |
| GPU texture lease leaks | 0 at end (validated by Gpu tier tests) |

## Running

```powershell
./scripts/test.ps1 -Tier Performance
```

Fast and Gpu tiers remain mandatory for every implementation unit.
