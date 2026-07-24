using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Models;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioEngineMapperTests
{
    [Fact]
    public void Studio_engine_ids_are_deterministic_and_scoped()
    {
        var first = StudioEngineIdMap.CanvasId("scene-main");
        var second = StudioEngineIdMap.CanvasId("scene-main");
        var layer = StudioEngineIdMap.DrawObjectId("scene-main");

        Assert.Equal(first, second);
        Assert.NotEqual(first.Value, layer.Value);
    }

    [Fact]
    public void Studio_engine_id_map_preserves_native_guid_strings()
    {
        var guid = Guid.NewGuid();

        Assert.Equal(guid, StudioEngineIdMap.CanvasId(guid.ToString()).Value);
        Assert.Equal(guid, StudioEngineIdMap.DrawObjectId(guid.ToString()).Value);
        Assert.Equal(guid, StudioEngineIdMap.SourceId(guid.ToString()).Value);
    }

    [Fact]
    public void Mutation_factory_maps_layer_visual_state_to_engine_patches()
    {
        var layer = CreateLayer();
        layer.Effects.Add(new StudioEffect
        {
            Id = "effect-chroma",
            Name = "Chroma Key",
            IsEnabled = true,
            KeyColor = "#24FF71",
            Tolerance = 0.45,
            EdgeSmooth = 0.12,
            Spill = 0.34
        });
        layer.Effects.Add(new StudioEffect
        {
            Id = "effect-blur",
            Name = "Desfoque",
            IsEnabled = false,
            Tolerance = 0.25
        });

        var transform = StudioSceneMutationFactory.SetLayerTransform(layer);
        var visibility = StudioSceneMutationFactory.SetLayerVisibility(layer);
        var opacity = StudioSceneMutationFactory.SetLayerOpacity(layer);
        var effects = StudioSceneMutationFactory.SetLayerEffects(layer);

        Assert.Equal(StudioEngineIdMap.DrawObjectId(layer.Id), transform.LayerId);
        Assert.Equal(160, transform.Transform.Position.X);
        Assert.Equal(90, transform.Transform.Position.Y);
        Assert.Equal(640, transform.Transform.Size.Width);
        Assert.Equal(360, transform.Transform.Size.Height);
        Assert.Equal(17, transform.Transform.RotationDegrees);
        Assert.Equal(StudioEngineIdMap.DrawObjectId(layer.Id), visibility.LayerId);
        Assert.True(visibility.IsVisible);
        Assert.Equal(0.74f, opacity.Opacity);

        Assert.Equal(2, effects.Effects.Count);
        var chroma = Assert.IsType<ChromaKeyEffect>(effects.Effects[0]);
        Assert.Equal(StudioEngineIdMap.EffectId("effect-chroma"), chroma.Id);
        Assert.True(chroma.Enabled);
        Assert.Equal(0, chroma.Order);
        Assert.Equal(0.45f, chroma.Similarity);
        Assert.Equal(0.12f, chroma.Smoothness);
        Assert.Equal(0.34f, chroma.SpillReduction);

        var blur = Assert.IsType<BlurEffect>(effects.Effects[1]);
        Assert.Equal(StudioEngineIdMap.EffectId("effect-blur"), blur.Id);
        Assert.False(blur.Enabled);
        Assert.Equal(1, blur.Order);
        Assert.Equal(16f, blur.Radius);
    }

    [Fact]
    public void Project_mapper_creates_valid_engine_project_from_studio_document()
    {
        var document = StudioMockDocumentFactory.Create();
        var mapper = new StudioProjectEngineMapper();

        var project = mapper.CreateProject(document);

        Assert.Equal(document.Scenes.Count, project.Canvases.Count);
        Assert.DoesNotContain(project.SourceDefinitions, source => source.Name == "Tarja inferior");
        Assert.Contains(project.SourceDefinitions, source =>
            source.Id == StudioEngineIdMap.SourceId("source-logo") &&
            source.TypeId == MediaSourceTypes.ImageFile);

        var mainCanvas = project.Canvases.Single(canvas => canvas.Id == StudioEngineIdMap.CanvasId("scene-main"));
        var logoLayer = Assert.IsType<SourceLayerDrawObject>(
            mainCanvas.Objects.Single(layer => layer.Id == StudioEngineIdMap.DrawObjectId("layer-logo")));
        Assert.Equal(StudioEngineIdMap.SourceId("source-logo"), logoLayer.SourceId);

        var textLayer = Assert.IsType<TextDrawObject>(
            mainCanvas.Objects.Single(layer => layer.Id == StudioEngineIdMap.DrawObjectId("layer-lower-third")));
        Assert.Equal("Tarja inferior", textLayer.Text);

        var rtmp = project.Outputs.Single(output => output.Id == StudioEngineIdMap.RenderOutputId("output-rtmp-twitch"));
        Assert.Equal(RenderOutputTypes.StreamingRtmp, rtmp.TypeId);
        Assert.Equal(StudioEngineIdMap.CanvasId("scene-main"), rtmp.CanvasId);
        Assert.Equal(OutputRouteTransitionKind.Fade, rtmp.RouteTransition.Kind);
        Assert.Equal(300, rtmp.RouteTransition.DurationMs);
    }

    [Fact]
    public void Project_mapper_rejects_layer_that_references_non_exportable_source()
    {
        var document = new StudioDocument();
        var scene = new StudioScene { Id = "scene", DisplayName = "Scene" };
        scene.Layers.Add(CreateLayer(sourceId: "missing-source"));
        document.Scenes.Add(scene);

        var ex = Assert.Throws<InvalidOperationException>(() => new StudioProjectEngineMapper().CreateProject(document));

        Assert.Contains("not exportable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_mapper_preserves_disabled_outputs_in_canonical_project()
    {
        var document = StudioMockDocumentFactory.Create();
        var disabled = document.Outputs.First();
        disabled.IsEnabled = false;
        var mapper = new StudioProjectEngineMapper();

        var project = mapper.CreateProject(document);
        var canonicalOutput = project.Outputs.Single(output =>
            output.Id == StudioEngineIdMap.RenderOutputId(disabled.Id));
        var restored = mapper.CreateDocument(project);

        Assert.False(canonicalOutput.Enabled);
        Assert.False(restored.Outputs.Single(output =>
            output.Id == canonicalOutput.Id.Value.ToString("D")).IsEnabled);
        Assert.Equal(document.Outputs.Count, project.Outputs.Count);
    }

    private static StudioLayer CreateLayer(string sourceId = "source-camera")
    {
        var layer = new StudioLayer
        {
            Id = "layer-camera",
            Name = "Camera",
            SourceId = sourceId,
            SourceName = "Camera",
            Type = "Source",
            Order = 1,
            IsVisible = true,
            BlendMode = StudioBlendMode.Alpha
        };
        layer.Transform.X = 160;
        layer.Transform.Y = 90;
        layer.Transform.Width = 640;
        layer.Transform.Height = 360;
        layer.Transform.RotationDegrees = 17;
        layer.Transform.Opacity = 74;
        return layer;
    }
}
