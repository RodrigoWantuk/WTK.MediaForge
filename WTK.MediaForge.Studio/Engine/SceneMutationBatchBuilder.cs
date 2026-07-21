using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.Engine;

public sealed class SceneMutationBatchBuilder(StudioProjectEngineMapper mapper)
{
    private readonly StudioProjectEngineMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

    public IReadOnlyList<SceneMutationPatch> Build(
        StudioDocument document,
        StudioScene original,
        StudioScene draft)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diff = StudioSceneDraftDiff.Compare(original, draft);
        var patches = new List<SceneMutationPatch>();
        var finalOrder = diff.FinalLayerOrder.ToList();

        foreach (var layer in diff.RemovedLayers)
            patches.Add(new SceneMutationPatch.RemoveLayer(StudioEngineIdMap.DrawObjectId(layer.Id)));

        foreach (var layer in diff.AddedLayers)
        {
            var index = finalOrder.IndexOf(layer.Id);
            patches.Add(new SceneMutationPatch.AddLayer(_mapper.CreateLayer(layer, document.Sources), index));
        }

        foreach (var change in diff.ChangedLayers)
            AppendLayerChanges(patches, change);

        var currentOrder = original.Layers.OrderBy(static layer => layer.Order)
            .Select(static layer => layer.Id)
            .Where(id => !diff.RemovedLayers.Any(layer => layer.Id == id))
            .ToList();
        foreach (var added in diff.AddedLayers.OrderBy(layer => finalOrder.IndexOf(layer.Id)))
            currentOrder.Insert(finalOrder.IndexOf(added.Id), added.Id);

        for (var index = 0; index < finalOrder.Count; index++)
        {
            var id = finalOrder[index];
            var currentIndex = currentOrder.IndexOf(id);
            if (currentIndex == index)
                continue;
            patches.Add(new SceneMutationPatch.SetLayerOrder(StudioEngineIdMap.DrawObjectId(id), index));
            currentOrder.RemoveAt(currentIndex);
            currentOrder.Insert(index, id);
        }

        return patches;
    }

    private static void AppendLayerChanges(
        ICollection<SceneMutationPatch> patches,
        StudioLayerDraftChange change)
    {
        var layer = change.Draft;
        if (change.Kind.HasFlag(StudioLayerChangeKind.Transform))
            patches.Add(StudioSceneMutationFactory.SetLayerTransform(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.Visibility))
            patches.Add(StudioSceneMutationFactory.SetLayerVisibility(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.Opacity))
            patches.Add(StudioSceneMutationFactory.SetLayerOpacity(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.Crop))
            patches.Add(StudioSceneMutationFactory.SetLayerCrop(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.BlendMode))
            patches.Add(StudioSceneMutationFactory.SetLayerBlendMode(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.Effects))
            patches.Add(StudioSceneMutationFactory.SetLayerEffects(layer));
        if (change.Kind.HasFlag(StudioLayerChangeKind.TypeSpecific))
            AppendTypeSpecificChange(patches, layer);
    }

    private static void AppendTypeSpecificChange(ICollection<SceneMutationPatch> patches, StudioLayer layer)
    {
        var layerId = StudioEngineIdMap.DrawObjectId(layer.Id);
        switch (layer.Type)
        {
            case "Text":
                patches.Add(new SceneMutationPatch.SetTextLayerContent(layerId, layer.SourceName));
                break;
            case "Canvas":
                patches.Add(new SceneMutationPatch.SetNestedCanvas(layerId, StudioEngineIdMap.CanvasId(layer.SourceId)));
                break;
            case "Solid":
                break;
            default:
                patches.Add(new SceneMutationPatch.SetLayerSource(layerId, StudioEngineIdMap.SourceId(layer.SourceId)));
                break;
        }
    }
}
