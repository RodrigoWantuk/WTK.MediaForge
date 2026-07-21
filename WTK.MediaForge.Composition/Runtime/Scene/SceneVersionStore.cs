using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class SceneVersionStore
{
    internal const int MaximumRetainedVersionsPerCanvas = 32;

    private readonly object _gate = new();
    private readonly Dictionary<CanvasId, Entry> _published = [];
    private readonly Dictionary<SceneVersionId, CanvasStateSnapshot> _snapshotsByVersion = [];
    private readonly Dictionary<SceneVersionId, CanvasId> _canvasByVersion = [];
    private readonly Dictionary<CanvasId, LinkedList<SceneVersionId>> _versionsByCanvas = [];
    private readonly Dictionary<SceneVersionId, int> _ownedPins = [];
    private HashSet<SceneVersionId> _bindingPins = [];
    private long _discardedVersions;
    private int _highWaterMark;

    public IReadOnlyDictionary<CanvasId, ScenePublishedState> PublishedStates
    {
        get
        {
            lock (_gate)
            {
                return _published.ToDictionary(
                    static pair => pair.Key,
                    static pair => new ScenePublishedState
                    {
                        CanvasId = pair.Key,
                        VersionId = pair.Value.VersionId,
                        Revision = pair.Value.Revision
                    });
            }
        }
    }

    public SceneVersionRetentionSnapshot GetRetentionSnapshot()
    {
        lock (_gate)
        {
            return new SceneVersionRetentionSnapshot
            {
                RetainedVersionCount = _snapshotsByVersion.Count,
                PinnedVersionCount = _snapshotsByVersion.Keys.Count(IsPinned),
                DiscardedVersionCount = _discardedVersions,
                HighWaterMark = _highWaterMark,
                MaximumRecentVersionsPerCanvas = MaximumRetainedVersionsPerCanvas
            };
        }
    }

    public SceneVersionId GetPublishedVersion(CanvasId canvasId)
    {
        lock (_gate)
        {
            return _published.TryGetValue(canvasId, out var entry)
                ? entry.VersionId
                : throw new InvalidOperationException($"Canvas {canvasId} does not have a published scene version.");
        }
    }

    public void Sync(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        lock (_gate)
        {
            _bindingPins = CollectExplicitBindings(projectState);
            var currentIds = projectState.Canvases.Select(static canvas => canvas.Id).ToHashSet();
            foreach (var stale in _published.Keys.Where(id => !currentIds.Contains(id)).ToArray())
                _published.Remove(stale);

            foreach (var canvas in projectState.Canvases)
            {
                var fingerprint = CanvasVisualStateFingerprint.Create(canvas);
                if (_published.TryGetValue(canvas.Id, out var existing) &&
                    string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                var versionId = SceneVersionId.New();
                _published[canvas.Id] = new Entry(
                    versionId,
                    fingerprint,
                    existing?.Revision + 1 ?? 1);
                RegisterVersionCore(canvas, versionId);
            }

            TrimAllCore();
        }
    }

    public IDisposable RegisterAndPinVersion(
        CanvasStateSnapshot canvas,
        SceneVersionId versionId,
        string owner)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (versionId.IsEmpty)
            throw new ArgumentException("A non-empty scene version is required.", nameof(versionId));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A scene version pin owner is required.", nameof(owner));

        lock (_gate)
        {
            RegisterVersionCore(canvas, versionId);
            _ownedPins.TryGetValue(versionId, out var count);
            _ownedPins[versionId] = checked(count + 1);
        }

        return new PinHandle(this, versionId, owner);
    }

    public IDisposable PinVersions(
        IEnumerable<SceneVersionId> versionIds,
        string owner)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A scene version pin owner is required.", nameof(owner));

        var versions = versionIds.Distinct().ToArray();
        if (versions.Length == 0)
            throw new ArgumentException("At least one scene version is required.", nameof(versionIds));

        lock (_gate)
        {
            var missing = versions.FirstOrDefault(version => !_snapshotsByVersion.ContainsKey(version));
            if (!missing.IsEmpty)
                throw new InvalidOperationException($"Scene version '{missing}' is not retained and cannot be pinned.");

            foreach (var version in versions)
            {
                _ownedPins.TryGetValue(version, out var count);
                _ownedPins[version] = checked(count + 1);
            }
        }

        return new PinSetHandle(this, versions, owner);
    }

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CreateVersionMap()
    {
        lock (_gate)
            return _published.ToDictionary(static pair => pair.Key, static pair => pair.Value.VersionId);
    }

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CreateVersionSnapshotMap()
    {
        lock (_gate)
            return _snapshotsByVersion.ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    private void RegisterVersionCore(CanvasStateSnapshot canvas, SceneVersionId versionId)
    {
        if (_snapshotsByVersion.TryGetValue(versionId, out var existing))
        {
            if (existing.Id != canvas.Id)
                throw new InvalidOperationException($"Scene version '{versionId}' is already owned by another canvas.");
            return;
        }

        _snapshotsByVersion.Add(versionId, canvas);
        _canvasByVersion.Add(versionId, canvas.Id);
        if (!_versionsByCanvas.TryGetValue(canvas.Id, out var versions))
        {
            versions = [];
            _versionsByCanvas.Add(canvas.Id, versions);
        }

        versions.AddLast(versionId);
        _highWaterMark = Math.Max(_highWaterMark, _snapshotsByVersion.Count);
    }

    private void ReleasePin(SceneVersionId versionId)
        => ReleasePins([versionId]);

    private void ReleasePins(IReadOnlyList<SceneVersionId> versionIds)
    {
        lock (_gate)
        {
            foreach (var versionId in versionIds)
            {
                if (!_ownedPins.TryGetValue(versionId, out var count) || count <= 0)
                    throw new InvalidOperationException($"Scene version pin '{versionId}' was released without ownership.");
            }

            foreach (var versionId in versionIds)
            {
                var count = _ownedPins[versionId];
                if (count == 1)
                    _ownedPins.Remove(versionId);
                else
                    _ownedPins[versionId] = count - 1;
            }

            TrimAllCore();
        }
    }

    private void TrimAllCore()
    {
        foreach (var canvasId in _versionsByCanvas.Keys.ToArray())
            TrimCanvasCore(canvasId);
    }

    private void TrimCanvasCore(CanvasId canvasId)
    {
        if (!_versionsByCanvas.TryGetValue(canvasId, out var versions))
            return;

        if (!_published.ContainsKey(canvasId))
        {
            RemoveUnpinnedVersionsCore(canvasId, versions, retainedRecentVersions: null);
        }
        else
        {
            var retainedRecentVersions = versions
                .Reverse()
                .Take(MaximumRetainedVersionsPerCanvas)
                .ToHashSet();
            RemoveUnpinnedVersionsCore(canvasId, versions, retainedRecentVersions);
        }

        if (versions.Count == 0)
            _versionsByCanvas.Remove(canvasId);
    }

    private void RemoveUnpinnedVersionsCore(
        CanvasId canvasId,
        LinkedList<SceneVersionId> versions,
        IReadOnlySet<SceneVersionId>? retainedRecentVersions)
    {
        var candidate = versions.First;
        while (candidate is not null)
        {
            var next = candidate.Next;
            if ((retainedRecentVersions is null || !retainedRecentVersions.Contains(candidate.Value)) &&
                !IsPinned(candidate.Value))
            {
                RemoveVersionCore(canvasId, versions, candidate);
            }

            candidate = next;
        }
    }

    private bool IsPinned(SceneVersionId versionId) =>
        _published.Values.Any(entry => entry.VersionId == versionId) ||
        _bindingPins.Contains(versionId) ||
        _ownedPins.ContainsKey(versionId);

    private void RemoveVersionCore(
        CanvasId canvasId,
        LinkedList<SceneVersionId> versions,
        LinkedListNode<SceneVersionId> node)
    {
        versions.Remove(node);
        _snapshotsByVersion.Remove(node.Value);
        _canvasByVersion.Remove(node.Value);
        _discardedVersions++;
    }

    private static HashSet<SceneVersionId> CollectExplicitBindings(ProjectStateSnapshot projectState)
    {
        var result = new HashSet<SceneVersionId>();
        foreach (var output in projectState.Outputs)
            AddBinding(output.SceneVersionBinding, result);

        foreach (var canvas in projectState.Canvases)
        {
            foreach (var nested in canvas.Objects.OfType<CanvasDrawObjectSnapshot>())
                AddBinding(nested.VersionBinding, result);
        }

        return result;
    }

    private static void AddBinding(SceneVersionBinding binding, ISet<SceneVersionId> result)
    {
        binding.Validate();
        if (binding.Kind == SceneVersionBindingKind.ExplicitVersion && binding.ExplicitVersionId is { } version)
            result.Add(version);
    }

    private sealed record Entry(SceneVersionId VersionId, string Fingerprint, long Revision);

    private sealed class PinHandle(SceneVersionStore owner, SceneVersionId versionId, string ownerName) : IDisposable
    {
        private SceneVersionStore? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?.ReleasePin(versionId);
        }

        public override string ToString() => $"{ownerName}:{versionId}";
    }

    private sealed class PinSetHandle(
        SceneVersionStore owner,
        IReadOnlyList<SceneVersionId> versionIds,
        string ownerName) : IDisposable
    {
        private SceneVersionStore? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?.ReleasePins(versionIds);
        }

        public override string ToString() => $"{ownerName}:{string.Join(',', versionIds)}";
    }
}
