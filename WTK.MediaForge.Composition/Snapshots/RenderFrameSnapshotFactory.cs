using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Snapshots;

public static class RenderFrameSnapshotFactory
{
    private const int MaxNestedCanvasDepth = 1;

    public static SnapshotBuildResult Build(ProjectStateSnapshot projectState, CompositionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(projectState);
        ArgumentNullException.ThrowIfNull(runtime);

        var diagnostics = new List<SnapshotDiagnostic>();
        var leasesBySource = new Dictionary<SourceId, GpuFrameLease>();
        var canvasLookup = projectState.Canvases.ToDictionary(c => c.Id);

        var canvases = projectState.Canvases
            .Select(canvas => BuildCanvas(canvas, canvasLookup, runtime, leasesBySource, diagnostics, nestingDepth: 0))
            .ToImmutableArray();

        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = projectState.Version,
            Canvases = canvases,
            Outputs = projectState.Outputs,
            FrameLeases = leasesBySource.Values.ToImmutableArray()
        };

        return SnapshotBuildResult.Create(snapshot, diagnostics.ToImmutableArray());
    }

    private static RenderCanvasSnapshot BuildCanvas(
        CanvasStateSnapshot canvas,
        IReadOnlyDictionary<CanvasId, CanvasStateSnapshot> canvasLookup,
        CompositionRuntime runtime,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        int nestingDepth)
    {
        var objects = canvas.Objects
            .Select(drawObject => BuildDrawObject(
                drawObject,
                canvasLookup,
                runtime,
                leasesBySource,
                diagnostics,
                nestingDepth))
            .ToImmutableArray();

        return new RenderCanvasSnapshot
        {
            Id = canvas.Id,
            Name = canvas.Name,
            Size = canvas.Size,
            BackgroundColor = canvas.BackgroundColor,
            Objects = objects
        };
    }

    private static RenderDrawObjectSnapshot BuildDrawObject(
        DrawObjectStateSnapshot drawObject,
        IReadOnlyDictionary<CanvasId, CanvasStateSnapshot> canvasLookup,
        CompositionRuntime runtime,
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
                SourceId = sourceLayer.SourceId,
                LayoutMode = sourceLayer.LayoutMode,
                ContentRotationOverride = sourceLayer.ContentRotationOverride,
                BoundFrame = TryAcquireSourceFrame(
                    sourceLayer.SourceId,
                    sourceLayer.Id,
                    runtime,
                    leasesBySource,
                    diagnostics)
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
                Text = text.Text,
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
                FillColor = solid.FillColor
            },
            CanvasDrawObjectSnapshot nested => BuildNestedCanvasDrawObject(
                nested,
                effectiveCrop,
                canvasLookup,
                runtime,
                leasesBySource,
                diagnostics,
                nestingDepth),
            _ => throw new NotSupportedException($"Unsupported draw object snapshot type: {drawObject.GetType().Name}.")
        };
    }

    private static RenderCanvasDrawObjectSnapshot BuildNestedCanvasDrawObject(
        CanvasDrawObjectSnapshot nested,
        NormalizedRect effectiveCrop,
        IReadOnlyDictionary<CanvasId, CanvasStateSnapshot> canvasLookup,
        CompositionRuntime runtime,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics,
        int nestingDepth)
    {
        RenderCanvasSnapshot? nestedCanvas = null;

        if (nestingDepth >= MaxNestedCanvasDepth)
        {
            diagnostics.Add(new SnapshotDiagnostic
            {
                Kind = SnapshotDiagnosticKind.NestedCanvasDepthExceeded,
                Message = $"Nested canvas depth exceeded for draw object '{nested.Name}'.",
                DrawObjectId = nested.Id,
                CanvasId = nested.NestedCanvasId
            });
        }
        else if (!canvasLookup.TryGetValue(nested.NestedCanvasId, out var nestedCanvasState))
        {
            diagnostics.Add(new SnapshotDiagnostic
            {
                Kind = SnapshotDiagnosticKind.NestedCanvasMissing,
                Message = $"Nested canvas {nested.NestedCanvasId} not found for draw object '{nested.Name}'.",
                DrawObjectId = nested.Id,
                CanvasId = nested.NestedCanvasId
            });
        }
        else
        {
            nestedCanvas = BuildCanvas(
                nestedCanvasState,
                canvasLookup,
                runtime,
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
            NestedCanvas = nestedCanvas
        };
    }

    private static GpuFrameReference? TryAcquireSourceFrame(
        SourceId sourceId,
        DrawObjectId drawObjectId,
        CompositionRuntime runtime,
        Dictionary<SourceId, GpuFrameLease> leasesBySource,
        List<SnapshotDiagnostic> diagnostics)
    {
        if (leasesBySource.TryGetValue(sourceId, out var existingLease))
            return existingLease.Frame;

        if (!runtime.TryGetFrameProvider(sourceId, out var provider))
        {
            diagnostics.Add(new SnapshotDiagnostic
            {
                Kind = SnapshotDiagnosticKind.SourceNotRegistered,
                Message = $"Source {sourceId} is not registered in the runtime.",
                SourceId = sourceId,
                DrawObjectId = drawObjectId
            });
            return null;
        }

        if (!provider.TryAcquireLatestFrame(out var lease))
        {
            diagnostics.Add(new SnapshotDiagnostic
            {
                Kind = SnapshotDiagnosticKind.SourceNoFrame,
                Message = $"No frame available for source {sourceId}.",
                SourceId = sourceId,
                DrawObjectId = drawObjectId
            });
            return null;
        }

        leasesBySource[sourceId] = lease;
        return lease.Frame;
    }
}
