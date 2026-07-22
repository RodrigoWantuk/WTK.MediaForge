using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.Engine;

[Flags]
public enum StudioLayerChangeKind
{
    None = 0,
    Transform = 1 << 0,
    Visibility = 1 << 1,
    Opacity = 1 << 2,
    Crop = 1 << 3,
    BlendMode = 1 << 4,
    Effects = 1 << 5,
    TypeSpecific = 1 << 6
}

public sealed record StudioLayerDraftChange(
    StudioLayer Original,
    StudioLayer Draft,
    StudioLayerChangeKind Kind);

public sealed record StudioSceneDraftDiff(
    IReadOnlyList<StudioLayer> RemovedLayers,
    IReadOnlyList<StudioLayer> AddedLayers,
    IReadOnlyList<StudioLayerDraftChange> ChangedLayers,
    IReadOnlyList<string> FinalLayerOrder)
{
    public static StudioSceneDraftDiff Compare(StudioScene original, StudioScene draft)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(draft);
        if (!string.Equals(original.Id, draft.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Draft diff requires two revisions of the same scene.");

        var originalOrdered = original.Layers.OrderBy(static layer => layer.Order).ToArray();
        var draftOrdered = draft.Layers.OrderBy(static layer => layer.Order).ToArray();
        var originalById = originalOrdered.ToDictionary(static layer => layer.Id, StringComparer.Ordinal);
        var draftById = draftOrdered.ToDictionary(static layer => layer.Id, StringComparer.Ordinal);
        var removed = originalOrdered
            .Where(layer => !draftById.TryGetValue(layer.Id, out var candidate) || candidate.Type != layer.Type)
            .ToArray();
        var added = draftOrdered
            .Where(layer => !originalById.TryGetValue(layer.Id, out var candidate) || candidate.Type != layer.Type)
            .ToArray();
        var changed = new List<StudioLayerDraftChange>();

        foreach (var current in draftOrdered)
        {
            if (!originalById.TryGetValue(current.Id, out var previous) || previous.Type != current.Type)
                continue;

            var kind = Classify(previous, current);
            if (kind != StudioLayerChangeKind.None)
                changed.Add(new StudioLayerDraftChange(previous, current, kind));
        }

        return new StudioSceneDraftDiff(
            removed,
            added,
            changed,
            draftOrdered.Select(static layer => layer.Id).ToArray());
    }

    private static StudioLayerChangeKind Classify(StudioLayer original, StudioLayer draft)
    {
        var result = StudioLayerChangeKind.None;
        if (original.Transform.X != draft.Transform.X ||
            original.Transform.Y != draft.Transform.Y ||
            original.Transform.Width != draft.Transform.Width ||
            original.Transform.Height != draft.Transform.Height ||
            original.Transform.RotationDegrees != draft.Transform.RotationDegrees ||
            original.Transform.PivotX != draft.Transform.PivotX ||
            original.Transform.PivotY != draft.Transform.PivotY)
        {
            result |= StudioLayerChangeKind.Transform;
        }
        if (original.IsVisible != draft.IsVisible)
            result |= StudioLayerChangeKind.Visibility;
        if (original.Transform.Opacity != draft.Transform.Opacity)
            result |= StudioLayerChangeKind.Opacity;
        if (original.Crop != draft.Crop)
            result |= StudioLayerChangeKind.Crop;
        if (original.BlendMode != draft.BlendMode)
            result |= StudioLayerChangeKind.BlendMode;
        if (!EffectsEqual(original.Effects, draft.Effects))
            result |= StudioLayerChangeKind.Effects;
        var typeSpecificChanged = draft.Type switch
        {
            "Text" => !string.Equals(original.SourceName, draft.SourceName, StringComparison.Ordinal),
            "Solid" => false,
            _ => !string.Equals(original.SourceId, draft.SourceId, StringComparison.Ordinal)
        };
        if (typeSpecificChanged)
        {
            result |= StudioLayerChangeKind.TypeSpecific;
        }
        return result;
    }

    private static bool EffectsEqual(
        IReadOnlyList<StudioEffect> left,
        IReadOnlyList<StudioEffect> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a.Id != b.Id || a.Name != b.Name || a.IsEnabled != b.IsEnabled || a.Kind != b.Kind ||
                a.KeyColor != b.KeyColor || a.Tolerance != b.Tolerance || a.Spill != b.Spill ||
                a.EdgeSmooth != b.EdgeSmooth || a.BlurRadius != b.BlurRadius ||
                a.Brightness != b.Brightness || a.Contrast != b.Contrast ||
                a.Saturation != b.Saturation || a.HueDegrees != b.HueDegrees)
            {
                return false;
            }
        }
        return true;
    }
}
