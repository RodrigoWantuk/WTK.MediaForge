using System.Collections.Immutable;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class RenderFrameSnapshotFactory
{
    private static int MaxNestedCanvasDepth => CanvasGraphLimits.MaxNestedCanvasDepth;

    public static SnapshotBuildResult Build(
        ProjectStateSnapshot projectState,
        CompositionRuntime runtime,
        IMediaForgeDiagnosticsSink? diagnosticsSink = null) =>
        Build(projectState, runtime, CreateDefaultContext(), diagnosticsSink);

    public static RenderFrameContext CreateDefaultContext(double targetFps = 60) =>
        new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1d / targetFps), targetFps, CancellationToken.None);

    public static SnapshotBuildResult Build(
        ProjectStateSnapshot projectState,
        CompositionRuntime runtime,
        RenderFrameContext context,
        IMediaForgeDiagnosticsSink? diagnosticsSink = null)
        => Build(projectState, runtime, context, outputRouteTransitions: null, diagnosticsSink);

    public static SnapshotBuildResult Build(
        ProjectStateSnapshot projectState,
        CompositionRuntime runtime,
        RenderFrameContext context,
        OutputRouteTransitionRuntime? outputRouteTransitions,
        IMediaForgeDiagnosticsSink? diagnosticsSink = null)
    {
        ArgumentNullException.ThrowIfNull(projectState);
        ArgumentNullException.ThrowIfNull(runtime);

        var diagnostics = new List<SnapshotDiagnostic>();
        var leasesBySource = new Dictionary<SourceId, GpuFrameLease>();

        try
        {
            var publishedContext = CanvasResolutionContext.From(projectState);
            var canvasesByKey = new Dictionary<ResolvedCanvasKey, RenderCanvasSnapshot>();

            foreach (var canvas in projectState.Canvases)
            {
                var renderCanvas = BuildCanvas(
                    canvas,
                    publishedContext,
                    SceneVersionBinding.Published,
                    runtime,
                    context,
                    leasesBySource,
                    diagnostics,
                    nestingDepth: 0);
                canvasesByKey.TryAdd(renderCanvas.ResolvedKey, renderCanvas);
            }

            var outputs = projectState.Outputs
                .Select(output => BuildOutputState(
                    output,
                    projectState,
                    publishedContext,
                    outputRouteTransitions,
                    runtime,
                    context,
                    leasesBySource,
                    diagnostics,
                    canvasesByKey))
                .ToImmutableArray();

            var snapshot = new RenderFrameSnapshot
            {
                ProjectStateVersion = projectState.Version,
                Canvases = canvasesByKey.Values.ToImmutableArray(),
                Outputs = outputs,
                FrameLeases = leasesBySource.Values.ToImmutableArray(),
                Context = context,
                Diagnostics = diagnosticsSink
            };

            return SnapshotBuildResult.Create(snapshot, diagnostics.ToImmutableArray(), diagnosticsSink);
        }
        catch
        {
            ReleaseLeases(leasesBySource.Values, diagnosticsSink);
            throw;
        }
    }

    private static RenderOutputStateSnapshot BuildOutputState(
        RenderOutputStateSnapshot output,
        ProjectStateSnapshot projectState,
        CanvasResolutionContext publishedContext,
        OutputRouteTransitionRuntime? outputRouteTransitions,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        Dictionary<ResolvedCanvasKey, RenderCanvasSnapshot> canvasesByKey)
    {
        var currentCanvas = BuildOutputCanvas(
            output,
            projectState,
            publishedContext,
            runtime,
            context,
            leasesBySource,
            diagnostics);
        canvasesByKey.TryAdd(currentCanvas.ResolvedKey, currentCanvas);
        var resolvedOutput = CloneOutputWithResolvedCanvas(output, currentCanvas.ResolvedKey);

        if (outputRouteTransitions is null ||
            !outputRouteTransitions.TryGetTransition(output.Id, out var transition) ||
            transition.Transition.Kind != OutputRouteTransitionKind.Fade ||
            transition.PreviousProjectState is null ||
            transition.Progress >= 1f)
        {
            return resolvedOutput;
        }

        var previousProjectState = transition.PreviousProjectState;
        var previousCanvasLookup = previousProjectState.Canvases.ToDictionary(static canvas => canvas.Id);
        if (!previousCanvasLookup.TryGetValue(transition.PreviousVersionGraph.RootCanvasId, out var previousCanvasState))
        {
            AddDiagnostic(
                diagnostics,
                SnapshotDiagnosticKind.NestedCanvasMissing,
                $"Previous route canvas {transition.PreviousVersionGraph.RootCanvasId} is no longer available for output transition.",
                canvasId: transition.PreviousVersionGraph.RootCanvasId);
            return resolvedOutput;
        }

        var previousCanvas = BuildCanvas(
            previousCanvasState,
            CanvasResolutionContext.From(previousProjectState),
            SceneVersionBinding.Published,
            runtime,
            context,
            leasesBySource,
            diagnostics,
            nestingDepth: 0);
        canvasesByKey.TryAdd(previousCanvas.ResolvedKey, previousCanvas);

        return CloneOutputWithTransition(
            resolvedOutput,
            transition.Transition.Kind,
            previousCanvas.Id,
            previousCanvas.ResolvedKey,
            transition.Progress);
    }

    private static RenderCanvasSnapshot BuildOutputCanvas(
        RenderOutputStateSnapshot output,
        ProjectStateSnapshot projectState,
        CanvasResolutionContext publishedContext,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics)
    {
        if (projectState.ResolvedOutputCanvases.TryGetValue(output.Id, out var resolved))
        {
            return BuildCanvas(
                resolved.RootCanvas,
                CanvasResolutionContext.From(resolved),
                resolved.Binding,
                runtime,
                context,
                leasesBySource,
                diagnostics,
                nestingDepth: 0,
                resolved.RootVersionId);
        }

        if (!publishedContext.CanvasLookup.TryGetValue(output.CanvasId, out var canvas))
            throw new InvalidOperationException($"Output '{output.Name}' references missing canvas '{output.CanvasId}'.");

        return BuildCanvas(
            canvas,
            publishedContext,
            SceneVersionBinding.Published,
            runtime,
            context,
            leasesBySource,
            diagnostics,
            nestingDepth: 0);
    }

    private static RenderOutputStateSnapshot CloneOutputWithResolvedCanvas(
        RenderOutputStateSnapshot output,
        ResolvedCanvasKey resolvedCanvasKey) =>
        new()
        {
            Id = output.Id,
            Name = output.Name,
            TypeId = output.TypeId,
            SchemaVersion = output.SchemaVersion,
            Settings = output.Settings,
            CanvasId = output.CanvasId,
            OutputSize = output.OutputSize,
            CanvasLayoutMode = output.CanvasLayoutMode,
            LetterboxColor = output.LetterboxColor,
            ColorSpace = output.ColorSpace,
            SceneVersionBinding = output.SceneVersionBinding,
            ResolvedCanvasKey = resolvedCanvasKey,
            RouteTransitionKind = output.RouteTransitionKind,
            PreviousCanvasId = output.PreviousCanvasId,
            PreviousResolvedCanvasKey = output.PreviousResolvedCanvasKey,
            RouteTransitionProgress = output.RouteTransitionProgress
        };

    private static RenderOutputStateSnapshot CloneOutputWithTransition(
        RenderOutputStateSnapshot output,
        OutputRouteTransitionKind transitionKind,
        CanvasId previousCanvasId,
        ResolvedCanvasKey previousResolvedCanvasKey,
        float progress) =>
        new()
        {
            Id = output.Id,
            Name = output.Name,
            TypeId = output.TypeId,
            SchemaVersion = output.SchemaVersion,
            Settings = output.Settings,
            CanvasId = output.CanvasId,
            OutputSize = output.OutputSize,
            CanvasLayoutMode = output.CanvasLayoutMode,
            LetterboxColor = output.LetterboxColor,
            ColorSpace = output.ColorSpace,
            SceneVersionBinding = output.SceneVersionBinding,
            ResolvedCanvasKey = output.ResolvedCanvasKey,
            RouteTransitionKind = transitionKind,
            PreviousCanvasId = previousCanvasId,
            PreviousResolvedCanvasKey = previousResolvedCanvasKey,
            RouteTransitionProgress = Math.Clamp(progress, 0f, 1f)
        };

    private static void ReleaseLeases(
        IEnumerable<GpuFrameLease> leases,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        foreach (var lease in leases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "render.lease_release_failed",
                    "Failed to release GPU frame lease during snapshot build rollback.",
                    nameof(RenderFrameSnapshotFactory),
                    ex);
            }
        }
    }

    private static RenderCanvasSnapshot BuildCanvas(
        CanvasStateSnapshot canvas,
        CanvasResolutionContext resolutionContext,
        SceneVersionBinding binding,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        int nestingDepth,
        SceneVersionId? versionOverride = null)
    {
        var objects = canvas.Objects
            .Select(drawObject => BuildDrawObject(
                drawObject,
                resolutionContext,
                runtime,
                context,
                leasesBySource,
                diagnostics,
                nestingDepth))
            .ToImmutableArray();

        var versionId = versionOverride ?? ResolveCanvasVersion(resolutionContext, canvas.Id, binding);
        var resolvedKey = ResolvedCanvasKey.Create(
            canvas.Id,
            versionId,
            binding,
            objects
                .OfType<RenderCanvasDrawObjectSnapshot>()
                .Where(static nested => nested.NestedResolvedCanvasKey is not null)
                .Select(static nested => nested.NestedResolvedCanvasKey!.Value));

        return new RenderCanvasSnapshot
        {
            Id = canvas.Id,
            Name = canvas.Name,
            Size = canvas.Size,
            BackgroundColor = canvas.BackgroundColor,
            VersionId = versionId,
            ResolvedKey = resolvedKey,
            Objects = objects
        };
    }

    private static RenderDrawObjectSnapshot BuildDrawObject(
        DrawObjectStateSnapshot drawObject,
        CanvasResolutionContext resolutionContext,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        int nestingDepth)
    {
        var effectiveCrop = drawObject.Crop ?? NormalizedRect.Full;

        return drawObject switch
        {
            SourceLayerDrawObjectSnapshot sourceLayer => new RenderSourceLayerDrawObjectSnapshot
            {
                Id = sourceLayer.Id,
                Name = sourceLayer.Name,
                Enabled = sourceLayer.Enabled,
                Transform = sourceLayer.Transform,
                EffectiveCrop = effectiveCrop,
                Opacity = sourceLayer.Opacity,
                BlendMode = sourceLayer.BlendMode,
                Effects = sourceLayer.Effects,
                SourceId = sourceLayer.SourceId,
                LayoutMode = sourceLayer.LayoutMode,
                LetterboxColor = sourceLayer.LetterboxColor,
                ContentRotationOverride = sourceLayer.ContentRotationOverride,
                BoundFrame = sourceLayer.Enabled
                    ? TryAcquireSourceFrame(
                        sourceLayer.SourceId,
                        sourceLayer.Id,
                        runtime,
                        context,
                        leasesBySource,
                        diagnostics)
                    : null
            },
            TextDrawObjectSnapshot text => new RenderTextDrawObjectSnapshot
            {
                Id = text.Id,
                Name = text.Name,
                Enabled = text.Enabled,
                Transform = text.Transform,
                EffectiveCrop = effectiveCrop,
                Opacity = text.Opacity,
                BlendMode = text.BlendMode,
                Effects = text.Effects,
                Text = text.Text,
                FontFamily = text.FontFamily,
                TextColor = text.TextColor,
                FontSize = text.FontSize
            },
            SolidDrawObjectSnapshot solid => new RenderSolidDrawObjectSnapshot
            {
                Id = solid.Id,
                Name = solid.Name,
                Enabled = solid.Enabled,
                Transform = solid.Transform,
                EffectiveCrop = effectiveCrop,
                Opacity = solid.Opacity,
                BlendMode = solid.BlendMode,
                Effects = solid.Effects,
                FillColor = solid.FillColor
            },
            CanvasDrawObjectSnapshot nested => nested.Enabled
                ? BuildNestedCanvasDrawObject(
                    nested,
                    effectiveCrop,
                    resolutionContext,
                    runtime,
                    context,
                    leasesBySource,
                    diagnostics,
                    nestingDepth)
                : CreateDisabledCanvasDrawObjectSnapshot(nested, resolutionContext, effectiveCrop),
            _ => throw new NotSupportedException($"Unsupported draw object snapshot type: {drawObject.GetType().Name}.")
        };
    }

    private static RenderCanvasDrawObjectSnapshot CreateDisabledCanvasDrawObjectSnapshot(
        CanvasDrawObjectSnapshot nested,
        CanvasResolutionContext resolutionContext,
        NormalizedRect effectiveCrop) =>
        new()
        {
            Id = nested.Id,
            Name = nested.Name,
            Enabled = nested.Enabled,
            Transform = nested.Transform,
            EffectiveCrop = effectiveCrop,
            Opacity = nested.Opacity,
            BlendMode = nested.BlendMode,
            Effects = nested.Effects,
            NestedCanvasId = nested.NestedCanvasId,
            VersionBinding = nested.VersionBinding,
            NestedCanvasVersionId = ResolveNestedVersion(resolutionContext, nested),
            NestedResolvedCanvasKey = null,
            NestedCanvas = null
        };

    private static RenderCanvasDrawObjectSnapshot BuildNestedCanvasDrawObject(
        CanvasDrawObjectSnapshot nested,
        NormalizedRect effectiveCrop,
        CanvasResolutionContext resolutionContext,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        int nestingDepth)
    {
        RenderCanvasSnapshot? nestedCanvas = null;

        if (nestingDepth >= MaxNestedCanvasDepth)
        {
            AddDiagnostic(
                diagnostics,
                SnapshotDiagnosticKind.NestedCanvasDepthExceeded,
                $"Nested canvas depth exceeded for draw object '{nested.Name}'.",
                drawObjectId: nested.Id,
                canvasId: nested.NestedCanvasId);
        }
        else if (!TryResolveNestedCanvasState(resolutionContext, nested, out var nestedCanvasState))
        {
            AddDiagnostic(
                diagnostics,
                SnapshotDiagnosticKind.NestedCanvasMissing,
                CreateNestedCanvasMissingMessage(nested),
                drawObjectId: nested.Id,
                canvasId: nested.NestedCanvasId);
        }
        else
        {
            nestedCanvas = BuildCanvas(
                nestedCanvasState,
                resolutionContext,
                nested.VersionBinding,
                runtime,
                context,
                leasesBySource,
                diagnostics,
                nestingDepth + 1);
        }

        return new RenderCanvasDrawObjectSnapshot
        {
            Id = nested.Id,
            Name = nested.Name,
            Enabled = nested.Enabled,
            Transform = nested.Transform,
            EffectiveCrop = effectiveCrop,
            Opacity = nested.Opacity,
            BlendMode = nested.BlendMode,
            Effects = nested.Effects,
            NestedCanvasId = nested.NestedCanvasId,
            VersionBinding = nested.VersionBinding,
            NestedCanvasVersionId = ResolveNestedVersion(resolutionContext, nested),
            NestedResolvedCanvasKey = nestedCanvas?.ResolvedKey,
            NestedCanvas = nestedCanvas
        };
    }

    private static Scenes.Editing.SceneVersionId? ResolveNestedVersion(
        CanvasResolutionContext resolutionContext,
        CanvasDrawObjectSnapshot nested) =>
        nested.VersionBinding.Kind == Scenes.Editing.SceneVersionBindingKind.ExplicitVersion
            ? nested.VersionBinding.ExplicitVersionId
            : resolutionContext.CanvasVersionIds.TryGetValue(nested.NestedCanvasId, out var version)
                ? version
                : null;

    private static bool TryResolveNestedCanvasState(
        CanvasResolutionContext resolutionContext,
        CanvasDrawObjectSnapshot nested,
        out CanvasStateSnapshot nestedCanvasState)
    {
        if (nested.VersionBinding.Kind == Scenes.Editing.SceneVersionBindingKind.ExplicitVersion)
        {
            if (nested.VersionBinding.ExplicitVersionId is { } explicitVersion &&
                resolutionContext.CanvasVersionSnapshots.TryGetValue(explicitVersion, out var versionedCanvas) &&
                versionedCanvas.Id == nested.NestedCanvasId)
            {
                nestedCanvasState = versionedCanvas;
                return true;
            }

            nestedCanvasState = default!;
            return false;
        }

        return resolutionContext.CanvasLookup.TryGetValue(nested.NestedCanvasId, out nestedCanvasState!);
    }

    private static string CreateNestedCanvasMissingMessage(CanvasDrawObjectSnapshot nested) =>
        nested.VersionBinding.Kind == Scenes.Editing.SceneVersionBindingKind.ExplicitVersion
            ? $"Nested canvas {nested.NestedCanvasId} explicit version {nested.VersionBinding.ExplicitVersionId} not found for draw object '{nested.Name}'."
            : $"Nested canvas {nested.NestedCanvasId} not found for draw object '{nested.Name}'.";

    private static SceneVersionId? ResolveCanvasVersion(
        CanvasResolutionContext resolutionContext,
        CanvasId canvasId,
        SceneVersionBinding binding) =>
        binding.Kind == SceneVersionBindingKind.ExplicitVersion
            ? binding.ExplicitVersionId
            : resolutionContext.CanvasVersionIds.TryGetValue(canvasId, out var version)
                ? version
                : null;

    private static GpuFrameReference? TryAcquireSourceFrame(
        SourceId sourceId,
        DrawObjectId drawObjectId,
        CompositionRuntime runtime,
        RenderFrameContext context,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics)
    {
        if (leasesBySource.TryGetValue(sourceId, out var existingLease))
            return existingLease.Frame;

        var acquireResult = runtime.TryAcquireFrame(sourceId, context.PresentationTime);
        switch (acquireResult.Status)
        {
            case SourceFrameAcquireStatus.Acquired:
                var lease = acquireResult.Lease
                    ?? throw new InvalidOperationException("Acquired source frame result did not include a lease.");
                leasesBySource[sourceId] = lease;
                return lease.Frame;

            case SourceFrameAcquireStatus.SourceNotRegistered:
                AddDiagnostic(
                    diagnostics,
                    SnapshotDiagnosticKind.SourceNotRegistered,
                    $"Source {sourceId} is not registered in the runtime.",
                    sourceId: sourceId,
                    drawObjectId: drawObjectId);
                return null;

            case SourceFrameAcquireStatus.SourceFailed:
                AddDiagnostic(
                    diagnostics,
                    SnapshotDiagnosticKind.SourceFailed,
                    $"Source {sourceId} failed while acquiring a frame.",
                    sourceId: sourceId,
                    drawObjectId: drawObjectId);
                return null;

            case SourceFrameAcquireStatus.NoFrameAvailable:
                AddDiagnostic(
                    diagnostics,
                    SnapshotDiagnosticKind.SourceNoFrame,
                    $"No frame available for source {sourceId}.",
                    sourceId: sourceId,
                    drawObjectId: drawObjectId);
                return null;

            default:
                throw new InvalidOperationException($"Unsupported source frame acquire status '{acquireResult.Status}'.");
        }
    }

    private static void AddDiagnostic(
        List<SnapshotDiagnostic> diagnostics,
        SnapshotDiagnosticKind kind,
        string message,
        SourceId? sourceId = null,
        CanvasId? canvasId = null,
        DrawObjectId? drawObjectId = null)
    {
        diagnostics.Add(new SnapshotDiagnostic
        {
            Kind = kind,
            Severity = SnapshotDiagnostic.DefaultSeverity(kind),
            Message = message,
            SourceId = sourceId,
            CanvasId = canvasId,
            DrawObjectId = drawObjectId
        });
    }

    private sealed record CanvasResolutionContext(
        IReadOnlyDictionary<CanvasId, CanvasStateSnapshot> CanvasLookup,
        IReadOnlyDictionary<CanvasId, SceneVersionId> CanvasVersionIds,
        IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CanvasVersionSnapshots)
    {
        public static CanvasResolutionContext From(ProjectStateSnapshot projectState) =>
            new(
                projectState.Canvases.ToDictionary(static canvas => canvas.Id),
                projectState.CanvasVersionIds,
                projectState.CanvasVersionSnapshots);

        public static CanvasResolutionContext From(ResolvedOutputCanvasStateSnapshot resolved) =>
            new(
                resolved.Canvases.ToDictionary(static canvas => canvas.Id),
                resolved.CanvasVersionIds,
                resolved.CanvasVersionSnapshots);
    }
}
