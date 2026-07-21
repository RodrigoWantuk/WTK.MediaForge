using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class SceneMutationBatchBuilderTests
{
    [Fact]
    public void Identical_hundred_layer_scene_produces_empty_batch()
    {
        var document = new StudioDocument();
        var original = new StudioScene { Id = "scene", DisplayName = "Scene" };
        for (var index = 0; index < 100; index++)
            original.Layers.Add(CreateLayer(index));
        var draft = SceneEditSessionService.CloneScene(original);

        var patches = new SceneMutationBatchBuilder(new StudioProjectEngineMapper())
            .Build(document, original, draft);

        Assert.Empty(patches);
    }

    [Fact]
    public void One_transform_change_produces_only_one_patch()
    {
        var document = new StudioDocument();
        var original = new StudioScene { Id = "scene", DisplayName = "Scene" };
        original.Layers.Add(CreateLayer(0));
        var draft = SceneEditSessionService.CloneScene(original);
        draft.Layers[0].Transform.X += 25;

        var patch = Assert.Single(new SceneMutationBatchBuilder(new StudioProjectEngineMapper())
            .Build(document, original, draft));

        Assert.IsType<SceneMutationPatch.SetLayerTransform>(patch);
    }

    [Fact]
    public void Reorder_produces_deterministic_minimal_order_patch()
    {
        var document = new StudioDocument();
        var original = new StudioScene { Id = "scene", DisplayName = "Scene" };
        original.Layers.Add(CreateLayer(0));
        original.Layers.Add(CreateLayer(1));
        var draft = SceneEditSessionService.CloneScene(original);
        draft.Layers[0].Order = 1;
        draft.Layers[1].Order = 0;

        var first = new SceneMutationBatchBuilder(new StudioProjectEngineMapper()).Build(document, original, draft);
        var second = new SceneMutationBatchBuilder(new StudioProjectEngineMapper()).Build(document, original, draft);

        var order = Assert.IsType<SceneMutationPatch.SetLayerOrder>(Assert.Single(first));
        Assert.Equal(0, order.NewIndex);
        Assert.Equal(StudioEngineIdMap.DrawObjectId("layer-1"), order.LayerId);
        Assert.Equal(first, second);
    }

    private static StudioLayer CreateLayer(int index)
    {
        var layer = new StudioLayer
        {
            Id = $"layer-{index}",
            Name = $"Layer {index}",
            Type = "Text",
            SourceName = $"Text {index}",
            Order = index
        };
        layer.Transform.Width = 320;
        layer.Transform.Height = 180;
        return layer;
    }
}
