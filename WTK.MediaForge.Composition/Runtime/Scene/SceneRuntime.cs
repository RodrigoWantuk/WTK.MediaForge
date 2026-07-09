using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class SceneRuntime
{
    private readonly List<ISceneRuntimeObserver> _observers = [];
    private readonly Dictionary<DrawObjectId, SceneLayerRuntimeState> _layers = [];
    private readonly Dictionary<SourceId, int> _resourceRefCounts = [];
    private readonly HashSet<DrawObjectId> _hiddenLayers = [];
    private readonly Dictionary<DrawObjectId, DrawObjectStateSnapshot> _previousDrawObjects = [];

    private ProjectStateSnapshot? _projectState;
    private SceneDirtyRegion _dirtyRegion = SceneDirtyRegion.Full;
    private MediaForgeRenderGraphPlan? _cachedRenderGraphPlan;
    private long _version;

    public IReadOnlyDictionary<SourceId, int> ResourceRefCounts => _resourceRefCounts;

    public void AddObserver(ISceneRuntimeObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
    }

    public void SyncFrom(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        _layers.Clear();
        _resourceRefCounts.Clear();

        var layerDirtyKinds = new Dictionary<DrawObjectId, SceneDirtyKind>();
        var structureChanged = false;
        var currentDrawObjects = new Dictionary<DrawObjectId, DrawObjectStateSnapshot>();

        foreach (var canvas in projectState.Canvases)
        {
            foreach (var drawObject in canvas.Objects)
            {
                currentDrawObjects[drawObject.Id] = drawObject;

                var isVisible = drawObject.Enabled && !_hiddenLayers.Contains(drawObject.Id);
                SourceId? sourceId = drawObject is SourceLayerDrawObjectSnapshot source
                    ? source.SourceId
                    : null;

                var dirtyKind = ComputeLayerDirtyKind(drawObject, ref structureChanged);
                _layers[drawObject.Id] = new SceneLayerRuntimeState
                {
                    LayerId = drawObject.Id,
                    IsVisible = isVisible,
                    BoundSourceId = sourceId,
                    DirtyKind = dirtyKind
                };

                if (dirtyKind != SceneDirtyKind.None)
                    layerDirtyKinds[drawObject.Id] = dirtyKind;

                if (sourceId is { } boundSource)
                {
                    _resourceRefCounts.TryGetValue(boundSource, out var count);
                    _resourceRefCounts[boundSource] = count + 1;
                }
            }
        }

        _hiddenLayers.RemoveWhere(layerId => !currentDrawObjects.ContainsKey(layerId));

        _projectState = projectState;
        _version++;

        _dirtyRegion = structureChanged
            ? SceneDirtyRegion.Full
            : new SceneDirtyRegion
            {
                GlobalKind = SceneDirtyKind.None,
                LayerDirtyKinds = layerDirtyKinds
            };

        if (_dirtyRegion.RequiresGraphRecompile)
            _cachedRenderGraphPlan = null;

        _previousDrawObjects.Clear();
        foreach (var pair in currentDrawObjects)
            _previousDrawObjects[pair.Key] = pair.Value;

        NotifyDirtyRegionChanged(_dirtyRegion);
    }

    public void SetLayerVisible(DrawObjectId layerId, bool isVisible)
    {
        if (!_layers.TryGetValue(layerId, out var existing))
            return;

        if (existing.IsVisible == isVisible)
            return;

        _layers[layerId] = new SceneLayerRuntimeState
        {
            LayerId = existing.LayerId,
            IsVisible = isVisible,
            BoundSourceId = existing.BoundSourceId,
            DirtyKind = existing.DirtyKind
        };

        if (isVisible)
            _hiddenLayers.Remove(layerId);
        else
            _hiddenLayers.Add(layerId);

        _version++;
        _dirtyRegion = SceneDirtyRegion.Full;
        _cachedRenderGraphPlan = null;

        if (!isVisible)
        {
            foreach (var observer in _observers)
                observer.OnHiddenLayerSkipped(layerId);
        }

        NotifyDirtyRegionChanged(_dirtyRegion);
    }

    public SceneRuntimeSnapshot CreateSnapshot()
    {
        if (_projectState is null)
            throw new InvalidOperationException("SceneRuntime has not been synchronized from project state.");

        var visibleProjectState = CreateVisibleProjectState(_projectState, _hiddenLayers);

        if (_cachedRenderGraphPlan is null)
            _cachedRenderGraphPlan = MediaForgeRenderGraphCompiler.Compile(visibleProjectState);

        return new SceneRuntimeSnapshot
        {
            ProjectState = visibleProjectState,
            Layers = _layers.ToDictionary(pair => pair.Key, pair => pair.Value),
            DirtyRegion = _dirtyRegion,
            Version = _version,
            CachedRenderGraphPlan = _cachedRenderGraphPlan,
            HiddenLayerIds = _hiddenLayers.ToHashSet()
        };
    }

    public SnapshotBuildResult BuildRenderSnapshot(
        CompositionRuntime runtime,
        RenderFrameContext context,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        var sceneSnapshot = CreateSnapshot();
        return RenderFrameSnapshotFactory.Build(
            sceneSnapshot.ProjectState,
            runtime,
            context,
            diagnostics);
    }

    private SceneDirtyKind ComputeLayerDirtyKind(
        DrawObjectStateSnapshot drawObject,
        ref bool structureChanged)
    {
        if (!_previousDrawObjects.TryGetValue(drawObject.Id, out var previous))
        {
            structureChanged = true;
            return SceneDirtyKind.Structure;
        }

        if (previous.GetType() != drawObject.GetType())
        {
            structureChanged = true;
            return SceneDirtyKind.Structure;
        }

        if (previous is SourceLayerDrawObjectSnapshot previousSource &&
            drawObject is SourceLayerDrawObjectSnapshot currentSource &&
            previousSource.SourceId != currentSource.SourceId)
        {
            structureChanged = true;
            return SceneDirtyKind.Structure;
        }

        if (!EffectsEqual(previous.Effects, drawObject.Effects))
            return SceneDirtyKind.Effects;

        if (!TransformEqual(previous.Transform, drawObject.Transform) ||
            previous.Opacity != drawObject.Opacity ||
            !CropEqual(previous.Crop, drawObject.Crop))
        {
            return SceneDirtyKind.Transform;
        }

        if (previous.Enabled != drawObject.Enabled)
            return SceneDirtyKind.Full;

        return SceneDirtyKind.None;
    }

    internal static ProjectStateSnapshot CreateVisibleProjectState(
        ProjectStateSnapshot projectState,
        IReadOnlySet<DrawObjectId> hiddenLayerIds)
    {
        var canvases = projectState.Canvases
            .Select(canvas => new CanvasStateSnapshot
            {
                Id = canvas.Id,
                Name = canvas.Name,
                Size = canvas.Size,
                BackgroundColor = canvas.BackgroundColor,
                Objects = canvas.Objects
                    .Where(drawObject =>
                        drawObject.Enabled &&
                        !hiddenLayerIds.Contains(drawObject.Id))
                    .ToImmutableArray()
            })
            .ToImmutableArray();

        return new ProjectStateSnapshot
        {
            Version = projectState.Version,
            Canvases = canvases,
            Outputs = projectState.Outputs,
            Sources = projectState.Sources
        };
    }

    private static bool TransformEqual(Transform2D left, Transform2D right) =>
        left.Position.X == right.Position.X &&
        left.Position.Y == right.Position.Y &&
        left.Size.Width == right.Size.Width &&
        left.Size.Height == right.Size.Height &&
        left.Pivot.X == right.Pivot.X &&
        left.Pivot.Y == right.Pivot.Y &&
        left.RotationDegrees == right.RotationDegrees;

    private static bool CropEqual(NormalizedRect? left, NormalizedRect? right)
    {
        if (left is null && right is null)
            return true;

        if (left is null || right is null)
            return false;

        return left.Value.Left == right.Value.Left &&
               left.Value.Top == right.Value.Top &&
               left.Value.Right == right.Value.Right &&
               left.Value.Bottom == right.Value.Bottom;
    }

    private static bool EffectsEqual(
        ImmutableArray<EffectStateSnapshot> left,
        ImmutableArray<EffectStateSnapshot> right) =>
        left.Length == right.Length &&
        left.Zip(right).All(pair => pair.First.Order == pair.Second.Order &&
                                    pair.First.Enabled == pair.Second.Enabled &&
                                    pair.First.GetType() == pair.Second.GetType());

    private void NotifyDirtyRegionChanged(SceneDirtyRegion region)
    {
        foreach (var observer in _observers)
            observer.OnSceneDirtyRegionChanged(region);
    }
}
