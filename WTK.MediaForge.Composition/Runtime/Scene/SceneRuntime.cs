using System.Collections.Immutable;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Scenes.Editing;
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
    private readonly PublishedSceneStateStore _publishedStore = new();
    private readonly DraftSceneStateStore _draftStore = new();

    private ProjectStateSnapshot? _projectState;
    private SceneDirtyRegion _dirtyRegion = SceneDirtyRegion.Full;
    private MediaForgeRenderGraphPlan? _cachedRenderGraphPlan;
    private long _version;

    public IReadOnlyDictionary<SourceId, int> ResourceRefCounts => _resourceRefCounts;

    public IReadOnlyDictionary<CanvasId, ScenePublishedState> PublishedStates => _publishedStore.States;

    public SceneVersionRetentionSnapshot VersionRetentionSnapshot => _publishedStore.RetentionSnapshot;

    public void AddObserver(ISceneRuntimeObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
    }

    public void SyncFrom(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        _draftStore.Clear();
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

        _publishedStore.Sync(projectState);
        _projectState = AttachPublishedVersions(projectState);
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

    public SceneRuntimeSnapshot CreateSnapshot(SceneVersionBinding binding)
    {
        binding.Validate();

        if (binding.Kind == SceneVersionBindingKind.Published)
            return CreateSnapshot();

        if (binding.Kind != SceneVersionBindingKind.Draft ||
            binding.DraftSessionId is not { } sessionId ||
            !_draftStore.TryGetProjectState(sessionId, out var draftProjectState) ||
            draftProjectState is null)
        {
            throw new InvalidOperationException($"Scene version binding '{binding}' cannot be resolved by the runtime.");
        }

        var visibleProjectState = CreateVisibleProjectState(draftProjectState, _hiddenLayers);
        return new SceneRuntimeSnapshot
        {
            ProjectState = visibleProjectState,
            Layers = _layers.ToDictionary(pair => pair.Key, pair => pair.Value),
            DirtyRegion = _dirtyRegion,
            Version = _version,
            CachedRenderGraphPlan = MediaForgeRenderGraphCompiler.Compile(visibleProjectState),
            HiddenLayerIds = _hiddenLayers.ToHashSet(),
            VersionBinding = binding
        };
    }

    public SceneVersionId GetPublishedVersion(CanvasId canvasId) =>
        _publishedStore.GetVersion(canvasId);

    public void UpsertDraft(SceneDraftState draftState, ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(draftState);
        ArgumentNullException.ThrowIfNull(projectState);

        var draftCanvas = projectState.Canvases.FirstOrDefault(canvas => canvas.Id == draftState.CanvasId)
            ?? throw new InvalidOperationException(
                $"Draft session '{draftState.SessionId}' does not contain canvas '{draftState.CanvasId}'.");
        var versionPin = _publishedStore.RegisterAndPinVersion(
            draftCanvas,
            draftState.DraftVersionId,
            $"draft:{draftState.SessionId}");
        try
        {
            _draftStore.Upsert(
                draftState.SessionId,
                AttachDraftVersion(projectState, draftState, _publishedStore),
                draftState,
                versionPin);
        }
        catch
        {
            versionPin.Dispose();
            throw;
        }
    }

    public bool TryGetDraft(SceneEditSessionId sessionId, out SceneDraftState? state) =>
        _draftStore.TryGet(sessionId, out state);

    public void DiscardDraft(SceneEditSessionId sessionId) =>
        _draftStore.Remove(sessionId);

    public SnapshotBuildResult BuildRenderSnapshot(
        CompositionRuntime runtime,
        RenderFrameContext context,
        IMediaForgeDiagnosticsSink? diagnostics = null)
        => BuildRenderSnapshot(runtime, context, outputRouteTransitions: null, diagnostics);

    public SnapshotBuildResult BuildRenderSnapshot(
        CompositionRuntime runtime,
        RenderFrameContext context,
        OutputRouteTransitionRuntime? outputRouteTransitions,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        var sceneSnapshot = CreateSnapshot();
        var renderProjectState = ResolveOutputVersionBindings(sceneSnapshot.ProjectState);
        return RenderFrameSnapshotFactory.Build(
            renderProjectState,
            runtime,
            context,
            outputRouteTransitions,
            diagnostics);
    }

    private ProjectStateSnapshot ResolveOutputVersionBindings(ProjectStateSnapshot publishedProjectState)
    {
        var resolvedOutputs = new Dictionary<RenderOutputId, ResolvedOutputCanvasStateSnapshot>();

        foreach (var output in publishedProjectState.Outputs)
        {
            output.SceneVersionBinding.Validate();
            if (output.SceneVersionBinding.Kind == SceneVersionBindingKind.Published)
                continue;

            var resolved = ResolveOutputCanvasVersion(publishedProjectState, output);
            resolvedOutputs.Add(output.Id, resolved);
        }

        return publishedProjectState with
        {
            ResolvedOutputCanvases = resolvedOutputs
        };
    }

    private ResolvedOutputCanvasStateSnapshot ResolveOutputCanvasVersion(
        ProjectStateSnapshot publishedProjectState,
        RenderOutputStateSnapshot output)
    {
        return output.SceneVersionBinding.Kind switch
        {
            SceneVersionBindingKind.Draft => ResolveDraftOutputCanvas(output),
            SceneVersionBindingKind.ExplicitVersion => ResolveExplicitOutputCanvas(publishedProjectState, output),
            _ => throw new InvalidOperationException(
                $"Output '{output.Name}' uses an unsupported root scene binding '{output.SceneVersionBinding.Kind}'.")
        };
    }

    private ResolvedOutputCanvasStateSnapshot ResolveDraftOutputCanvas(RenderOutputStateSnapshot output)
    {
        var sessionId = output.SceneVersionBinding.DraftSessionId
            ?? throw new InvalidOperationException($"Output '{output.Name}' has an invalid draft scene binding.");
        if (!_draftStore.TryGetProjectState(sessionId, out var draftProjectState) || draftProjectState is null)
        {
            throw new InvalidOperationException(
                $"Output '{output.Name}' references draft session '{sessionId}' which is no longer available.");
        }

        var canvas = draftProjectState.Canvases.FirstOrDefault(candidate => candidate.Id == output.CanvasId)
            ?? throw new InvalidOperationException(
                $"Output '{output.Name}' draft session '{sessionId}' does not contain canvas '{output.CanvasId}'.");
        var versionId = draftProjectState.CanvasVersionIds.TryGetValue(canvas.Id, out var version)
            ? version
            : throw new InvalidOperationException(
                $"Output '{output.Name}' draft session '{sessionId}' does not provide a version for canvas '{canvas.Id}'.");
        return new ResolvedOutputCanvasStateSnapshot
        {
            RootCanvas = canvas,
            RootVersionId = versionId,
            Binding = output.SceneVersionBinding,
            Canvases = draftProjectState.Canvases,
            CanvasVersionIds = draftProjectState.CanvasVersionIds,
            CanvasVersionSnapshots = draftProjectState.CanvasVersionSnapshots
        };
    }

    private static ResolvedOutputCanvasStateSnapshot ResolveExplicitOutputCanvas(
        ProjectStateSnapshot publishedProjectState,
        RenderOutputStateSnapshot output)
    {
        var versionId = output.SceneVersionBinding.ExplicitVersionId
            ?? throw new InvalidOperationException($"Output '{output.Name}' has an invalid explicit scene binding.");
        if (!publishedProjectState.CanvasVersionSnapshots.TryGetValue(versionId, out var canvas) ||
            canvas.Id != output.CanvasId)
        {
            throw new InvalidOperationException(
                $"Output '{output.Name}' explicit version '{versionId}' does not resolve canvas '{output.CanvasId}'.");
        }

        return new ResolvedOutputCanvasStateSnapshot
        {
            RootCanvas = canvas,
            RootVersionId = versionId,
            Binding = output.SceneVersionBinding,
            Canvases = publishedProjectState.Canvases,
            CanvasVersionIds = publishedProjectState.CanvasVersionIds,
            CanvasVersionSnapshots = publishedProjectState.CanvasVersionSnapshots
        };
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
            Sources = projectState.Sources,
            CanvasVersionIds = projectState.CanvasVersionIds,
            CanvasVersionSnapshots = projectState.CanvasVersionSnapshots,
            ResolvedOutputCanvases = projectState.ResolvedOutputCanvases
        };
    }

    private ProjectStateSnapshot AttachPublishedVersions(ProjectStateSnapshot projectState) =>
        projectState with
        {
            CanvasVersionIds = _publishedStore.CreateVersionMap(),
            CanvasVersionSnapshots = _publishedStore.CreateVersionSnapshotMap()
        };

    private static ProjectStateSnapshot AttachDraftVersion(
        ProjectStateSnapshot projectState,
        SceneDraftState draftState,
        PublishedSceneStateStore publishedStore)
    {
        var map = publishedStore.CreateVersionMap().ToDictionary(pair => pair.Key, pair => pair.Value);
        var snapshotsByVersion = publishedStore.CreateVersionSnapshotMap()
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var pair in projectState.CanvasVersionIds)
            map[pair.Key] = pair.Value;

        foreach (var pair in projectState.CanvasVersionSnapshots)
            snapshotsByVersion[pair.Key] = pair.Value;

        map[draftState.CanvasId] = draftState.DraftVersionId;

        var draftCanvas = projectState.Canvases.FirstOrDefault(canvas => canvas.Id == draftState.CanvasId);
        if (draftCanvas is not null)
            snapshotsByVersion[draftState.DraftVersionId] = draftCanvas;

        return projectState with
        {
            CanvasVersionIds = map,
            CanvasVersionSnapshots = snapshotsByVersion
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
        EffectStateFingerprint.SequenceEquals(left, right);

    private void NotifyDirtyRegionChanged(SceneDirtyRegion region)
    {
        foreach (var observer in _observers)
            observer.OnSceneDirtyRegionChanged(region);
    }
}
