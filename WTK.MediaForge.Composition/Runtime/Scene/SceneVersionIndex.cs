using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class SceneVersionIndex
{
    internal const int MaximumRetainedVersionsPerCanvas = 32;

    private readonly Dictionary<CanvasId, Entry> _entries = [];
    private readonly Dictionary<SceneVersionId, CanvasStateSnapshot> _snapshotsByVersion = [];
    private readonly Dictionary<CanvasId, LinkedList<SceneVersionId>> _versionsByCanvas = [];

    public IReadOnlyDictionary<CanvasId, ScenePublishedState> PublishedStates =>
        _entries.ToDictionary(
            static pair => pair.Key,
            static pair => new ScenePublishedState
            {
                CanvasId = pair.Key,
                VersionId = pair.Value.VersionId,
                Revision = pair.Value.Revision
            });

    public SceneVersionId GetPublishedVersion(CanvasId canvasId) =>
        _entries.TryGetValue(canvasId, out var entry)
            ? entry.VersionId
            : throw new InvalidOperationException($"Canvas {canvasId} does not have a published scene version.");

    public void Sync(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        var pinnedVersions = CollectExplicitBindings(projectState);
        var currentIds = projectState.Canvases.Select(static canvas => canvas.Id).ToHashSet();
        foreach (var stale in _entries.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            if (_versionsByCanvas.Remove(stale, out var staleVersions))
            {
                foreach (var version in staleVersions)
                    _snapshotsByVersion.Remove(version);
            }
            _entries.Remove(stale);
        }

        foreach (var canvas in projectState.Canvases)
        {
            var fingerprint = CreateFingerprint(canvas);
            if (_entries.TryGetValue(canvas.Id, out var existing) &&
                string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            var entry = new Entry(
                SceneVersionId.New(),
                fingerprint,
                existing?.Revision + 1 ?? 1);
            _entries[canvas.Id] = entry;
            _snapshotsByVersion[entry.VersionId] = canvas;
            if (!_versionsByCanvas.TryGetValue(canvas.Id, out var versions))
            {
                versions = [];
                _versionsByCanvas.Add(canvas.Id, versions);
            }

            versions.AddLast(entry.VersionId);
            TrimVersions(canvas.Id, pinnedVersions);
        }
    }

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CreateVersionMap() =>
        _entries.ToDictionary(static pair => pair.Key, static pair => pair.Value.VersionId);

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CreateVersionSnapshotMap() =>
        _snapshotsByVersion.ToDictionary(static pair => pair.Key, static pair => pair.Value);

    private void TrimVersions(CanvasId canvasId, IReadOnlySet<SceneVersionId> pinnedVersions)
    {
        if (!_versionsByCanvas.TryGetValue(canvasId, out var versions))
            return;

        var currentVersion = _entries[canvasId].VersionId;
        while (versions.Count > MaximumRetainedVersionsPerCanvas)
        {
            var removable = versions.First;
            while (removable is not null &&
                   (removable.Value == currentVersion || pinnedVersions.Contains(removable.Value)))
            {
                removable = removable.Next;
            }

            if (removable is null)
                return;

            _snapshotsByVersion.Remove(removable.Value);
            versions.Remove(removable);
        }
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
}
