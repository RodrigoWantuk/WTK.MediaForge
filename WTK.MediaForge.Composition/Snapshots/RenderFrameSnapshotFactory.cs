using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Sources;
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
        IMediaForgeDiagnosticsSink? diagnosticsSink = null)
    {
        ArgumentNullException.ThrowIfNull(projectState);
        ArgumentNullException.ThrowIfNull(runtime);

        var diagnostics = new List<SnapshotDiagnostic>();
        var leasesBySource = new Dictionary<SourceId, GpuFrameLease>();

        try
        {
            var canvasLookup = projectState.Canvases.ToDictionary(c => c.Id);

            var canvases = projectState.Canvases
                .Select(canvas => BuildCanvas(
                    canvas,
                    canvasLookup,
                    runtime,
                    leasesBySource,
                    diagnostics,
                    nestingDepth: 0))
                .ToImmutableArray();

            var snapshot = new RenderFrameSnapshot
            {
                ProjectStateVersion = projectState.Version,
                Canvases = canvases,
                Outputs = projectState.Outputs,
                FrameLeases = leasesBySource.Values.ToImmutableArray(),
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
                Effects = sourceLayer.Effects,
                SourceId = sourceLayer.SourceId,
                LayoutMode = sourceLayer.LayoutMode,
                ContentRotationOverride = sourceLayer.ContentRotationOverride,
                BoundFrame = sourceLayer.Enabled
                    ? TryAcquireSourceFrame(
                        sourceLayer.SourceId,
                        sourceLayer.Id,
                        runtime,
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
                    canvasLookup,
                    runtime,
                    leasesBySource,
                    diagnostics,
                    nestingDepth)
                : CreateDisabledCanvasDrawObjectSnapshot(nested, effectiveCrop),
            _ => throw new NotSupportedException($"Unsupported draw object snapshot type: {drawObject.GetType().Name}.")
        };
    }

    private static RenderCanvasDrawObjectSnapshot CreateDisabledCanvasDrawObjectSnapshot(
        CanvasDrawObjectSnapshot nested,
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
            NestedCanvas = null
        };

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
            AddDiagnostic(
                diagnostics,
                SnapshotDiagnosticKind.NestedCanvasDepthExceeded,
                $"Nested canvas depth exceeded for draw object '{nested.Name}'.",
                drawObjectId: nested.Id,
                canvasId: nested.NestedCanvasId);
        }
        else if (!canvasLookup.TryGetValue(nested.NestedCanvasId, out var nestedCanvasState))
        {
            AddDiagnostic(
                diagnostics,
                SnapshotDiagnosticKind.NestedCanvasMissing,
                $"Nested canvas {nested.NestedCanvasId} not found for draw object '{nested.Name}'.",
                drawObjectId: nested.Id,
                canvasId: nested.NestedCanvasId);
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
            Effects = nested.Effects,
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

        var acquireResult = runtime.TryAcquireFrame(sourceId, TimeSpan.Zero);
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
}
