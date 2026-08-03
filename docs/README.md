# Documentation Map

This directory contains product contracts, implementation guidance, validation rules, operational instructions, and historical evidence for WTK MediaForge.

## Authority order

When documents disagree, use the following order:

1. [`ROADMAP_CURRENT.md`](ROADMAP_CURRENT.md) — current product reality, active execution order, and explicitly deferred scope.
2. [`AI_CONTEXT.md`](AI_CONTEXT.md) — compact technical context and non-negotiable engineering rules.
3. [`PRODUCT_MODEL.md`](PRODUCT_MODEL.md) — serializable product model and authoring semantics.
4. [`PUBLIC_API.md`](PUBLIC_API.md) — supported public authoring, runtime, capability, source, output, and sink boundaries.
5. [`../ARCHITECTURE.md`](../ARCHITECTURE.md) — runtime ownership, physical execution, platform boundaries, and dependency direction.
6. Capability matrices:
   - [`GPU_MEDIA_SUPPORT_MATRIX.md`](GPU_MEDIA_SUPPORT_MATRIX.md)
   - [`AUDIO_SUPPORT_MATRIX.md`](AUDIO_SUPPORT_MATRIX.md)
7. Validation and contribution rules:
   - [`BUILD_AND_RELEASE.md`](BUILD_AND_RELEASE.md)
   - [`REVIEW_CHECKLIST.md`](REVIEW_CHECKLIST.md)
   - [`../AGENTS.md`](../AGENTS.md)
   - [`../CONTRIBUTING.md`](../CONTRIBUTING.md)

Files under `docs/history` are evidence only. They must not be used as active requirements, readiness entrypoints, or implementation order when they conflict with the normative set above.

## Active delivery milestone

The functional API/Studio milestone document defines the current integration
checkpoint for the public API and Avalonia Studio.

In this repository, the integration checkpoint is not a product capability
status. It does not relax the GPU Media Transport Law, capability truth,
deterministic ownership, cross-platform architecture, validation requirements,
or final-product model.

## Subject guides

### Studio

- [`UI_STUDIO_DESIGN.md`](UI_STUDIO_DESIGN.md)
- [`UI_IMPLEMENTATION_PLAN.md`](UI_IMPLEMENTATION_PLAN.md)
- [`UI_ACCEPTANCE_CHECKLIST.md`](UI_ACCEPTANCE_CHECKLIST.md)

### Audio

- [`AUDIO_ARCHITECTURE.md`](AUDIO_ARCHITECTURE.md)
- [`AUDIO_SUPPORT_MATRIX.md`](AUDIO_SUPPORT_MATRIX.md)

### Remote Scene

- [`REMOTE_SCENE.md`](REMOTE_SCENE.md)
- [`SIGNALING_DEPLOYMENT.md`](SIGNALING_DEPLOYMENT.md)

### Media, licensing, and platform support

- [`GPU_MEDIA_SUPPORT_MATRIX.md`](GPU_MEDIA_SUPPORT_MATRIX.md)
- [`MEDIA_LICENSE_POLICY.md`](MEDIA_LICENSE_POLICY.md)
- [`MACOS_VULKAN_METAL_INTEROP.md`](MACOS_VULKAN_METAL_INTEROP.md)

## Documentation maintenance rules

Every implementation unit that changes behavior, support status, public API, ownership, validation, or execution order must update the matching normative document in the same unit.

Use the following terms consistently:

- `Implemented`: code exists and is exercised by automated tests.
- `Experimental`: a real backend exists, but product promotion still depends on explicit proof or sustained qualification.
- `Supported`: the implementation and its required product proof are both current and passed for the reported environment.
- `Unavailable`: the runtime cannot offer the feature and exposes a concrete reason.
- `Planned`: a product contract or approved direction exists, but the implementation does not.
- `Blocked`: implementation or promotion is prevented by a named technical, legal, hardware, or proof dependency.
- `Deferred`: intentionally outside the active execution order.

Do not use prototype code, contract-only native libraries, model serialization, nominal hardware names, or skipped tests as capability evidence.
