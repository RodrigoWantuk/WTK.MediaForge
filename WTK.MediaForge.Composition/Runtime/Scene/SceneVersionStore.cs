using System.Security.Cryptography;
using System.Text;
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
                var fingerprint = CreateFingerprint(canvas);
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
    {
        lock (_gate)
        {
            if (!_ownedPins.TryGetValue(versionId, out var count) || count <= 0)
                throw new InvalidOperationException($"Scene version pin '{versionId}' was released without ownership.");

            if (count == 1)
                _ownedPins.Remove(versionId);
            else
                _ownedPins[versionId] = count - 1;

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

        while (versions.Count > MaximumRetainedVersionsPerCanvas)
        {
            var removable = versions.First;
            while (removable is not null && IsPinned(removable.Value))
                removable = removable.Next;

            if (removable is null)
                return;

            RemoveVersionCore(canvasId, versions, removable);
        }

        if (!_published.ContainsKey(canvasId) && versions.All(version => !IsPinned(version)))
        {
            while (versions.First is { } removable)
                RemoveVersionCore(canvasId, versions, removable);
        }

        if (versions.Count == 0)
            _versionsByCanvas.Remove(canvasId);
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

    private static string CreateFingerprint(CanvasStateSnapshot canvas)
    {
        var builder = new StringBuilder();
        builder.Append(canvas.Id.Value).Append('|')
            .Append(canvas.Name).Append('|')
            .Append(canvas.Size.Width).Append('x').Append(canvas.Size.Height).Append('|')
            .Append(canvas.BackgroundColor.R).Append(',')
            .Append(canvas.BackgroundColor.G).Append(',')
            .Append(canvas.BackgroundColor.B).Append(',')
            .Append(canvas.BackgroundColor.A);

        var order = 0;
        foreach (var drawObject in canvas.Objects)
        {
            builder.Append('|').Append(order++).Append(':').Append(drawObject.GetType().Name)
                .Append(':').Append(drawObject.Id.Value)
                .Append(':').Append(drawObject.Name)
                .Append(':').Append(drawObject.Enabled)
                .Append(':').Append(drawObject.Transform)
                .Append(':').Append(drawObject.Opacity)
                .Append(':').Append(drawObject.BlendMode)
                .Append(':').Append(drawObject.Crop?.ToString() ?? "crop-null");

            switch (drawObject)
            {
                case SourceLayerDrawObjectSnapshot source:
                    builder.Append(":source=").Append(source.SourceId.Value);
                    break;
                case CanvasDrawObjectSnapshot nested:
                    builder.Append(":canvas=").Append(nested.NestedCanvasId.Value)
                        .Append(":binding=").Append(nested.VersionBinding);
                    break;
                case TextDrawObjectSnapshot text:
                    builder.Append(":text=").Append(text.Text)
                        .Append(":font=").Append(text.FontFamily)
                        .Append(":size=").Append(text.FontSize);
                    break;
                case SolidDrawObjectSnapshot solid:
                    builder.Append(":solid=").Append(solid.FillColor.R).Append(',')
                        .Append(solid.FillColor.G).Append(',')
                        .Append(solid.FillColor.B).Append(',')
                        .Append(solid.FillColor.A);
                    break;
            }

            foreach (var effect in drawObject.Effects.OrderBy(static effect => effect.Order))
                builder.Append(":effect=").Append(EffectStateFingerprint.Create(effect));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
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
}
