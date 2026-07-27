using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderGraphCompilerTests
{
    [Fact]
    public void Canvas_effects_run_after_the_fully_composed_canvas()
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source);
        scene.Effects.Add(new ColorCorrectionEffect
        {
            Name = "Program grade",
            Brightness = 0.1f,
            Contrast = 1.1f
        });

        var project = builder
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .AddText(scene, "Program")
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        var canvas = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender);
        var effect = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasEffectChain);
        var output = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.OutputPass);

        Assert.Equal([canvas.Key], effect.Dependencies);
        Assert.Equal([effect.Key], output.Dependencies);
    }

    [Fact]
    public void Compatible_layers_share_source_effect_result_before_fanout()
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source);
        source.Effects.Add(new ColorCorrectionEffect { Name = "Shared grade", Contrast = 1.1f });
        var project = builder
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 960, 540))
            .AddSourceLayer(scene, source, layer => layer.SetBounds(960, 540, 960, 540))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        var sourceEffect = Assert.Single(
            graph.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.SourceEffectChain);
        Assert.Contains("frame:-1", sourceEffect.Key, StringComparison.Ordinal);
        Assert.Contains("stack:", sourceEffect.Key, StringComparison.Ordinal);
        Assert.Contains("format:PROJECT_SOURCE", sourceEffect.Key, StringComparison.Ordinal);
        Assert.Contains("resolution:1920x1080", sourceEffect.Key, StringComparison.Ordinal);
        Assert.Contains("color-space:Srgb", sourceEffect.Key, StringComparison.Ordinal);
        var canvas = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender);
        var sourceLayers = graph.Nodes
            .Where(node => node.Kind == MediaForgeRenderGraphNodeKind.SourceLayer)
            .ToArray();
        Assert.Equal(2, sourceLayers.Length);
        Assert.All(sourceLayers, node => Assert.Equal([sourceEffect.Key], node.Dependencies));
        Assert.Equal(2, canvas.Dependencies.Count(key => sourceLayers.Select(node => node.Key).Contains(key)));
    }

    [Fact]
    public void Same_scene_to_multiple_outputs_renders_canvas_once()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Debug", scene, 1920, 1080, out _)
            .PreviewOutput("Preview", scene, 1280, 720, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.SourceFrame));
        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.OutputPass));
    }

    [Fact]
    public void Same_layer_effect_chain_across_scenes_keeps_local_effect_nodes()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Preview", 1280, 720, out var preview)
            .Scene("Program", 1920, 1080, out var program)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                preview,
                source,
                layer => layer
                    .SetBounds(0, 0, 1280, 720)
                    .AddChromaKey(ColorRgba.From(0, 1, 0, 1)))
            .AddSourceLayer(
                program,
                source,
                layer => layer
                    .SetBounds(100, 100, 640, 360)
                    .AddChromaKey(ColorRgba.From(0, 1, 0, 1)))
            .OffscreenOutput("Preview out", preview, 1280, 720, out _)
            .OffscreenOutput("Program out", program, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.SourceFrame));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.LayerEffectChain));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.OutputPass));
    }

    [Fact]
    public void Same_source_and_blur_chain_across_scenes_uses_placement_specific_effect_nodes()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Preview", 1280, 720, out var preview)
            .Scene("Program", 1920, 1080, out var program)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                preview,
                source,
                layer => layer
                    .SetBounds(0, 0, 1280, 720)
                    .AddBlur(6f))
            .AddSourceLayer(
                program,
                source,
                layer => layer
                    .SetBounds(100, 100, 640, 360)
                    .AddBlur(6f))
            .OffscreenOutput("Preview out", preview, 1280, 720, out _)
            .OffscreenOutput("Program out", program, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.SourceFrame));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.LayerEffectChain));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.OutputPass));
    }

    [Fact]
    public void Different_blur_configuration_does_not_reuse_effect_node()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Preview", 1280, 720, out var preview)
            .Scene("Program", 1920, 1080, out var program)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                preview,
                source,
                layer => layer
                    .SetBounds(0, 0, 1280, 720)
                    .AddBlur(4f))
            .AddSourceLayer(
                program,
                source,
                layer => layer
                    .SetBounds(100, 100, 640, 360)
                    .AddBlur(12f))
            .OffscreenOutput("Preview out", preview, 1280, 720, out _)
            .OffscreenOutput("Program out", program, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.SourceFrame));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.LayerEffectChain));
    }

    [Fact]
    public void Canvas_depends_on_effect_chain_instead_of_raw_source_when_effects_are_enabled()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                scene,
                source,
                layer => layer
                    .SetBounds(0, 0, 1920, 1080)
                    .AddChromaKey(ColorRgba.From(0, 1, 0, 1)))
            .OffscreenOutput("Program", scene, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);
        var sourceNode = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.SourceFrame);
        var effectNode = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.LayerEffectChain);
        var sourceLayerNode = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.SourceLayer);
        var canvasNode = Assert.Single(graph.Nodes, node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender);

        Assert.Contains(sourceNode.Key, effectNode.Dependencies);
        Assert.Equal([effectNode.Key], sourceLayerNode.Dependencies);
        Assert.Contains(sourceLayerNode.Key, canvasNode.Dependencies);
        Assert.DoesNotContain(sourceNode.Key, canvasNode.Dependencies);
    }

    [Fact]
    public void Different_output_sizes_share_canvas_render_and_split_output_passes()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Full", scene, 1920, 1080, out _)
            .OffscreenOutput("Half", scene, 960, 540, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(1, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.OutputPass));
    }

    [Fact]
    public void Nested_canvas_dependency_is_reused_by_parent_and_direct_output()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Reusable", 640, 360, out var reusable)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(reusable, source, layer => layer.SetBounds(0, 0, 640, 360))
            .Scene("Program", 1920, 1080, out var program)
            .AddCanvasLayer(program, reusable, layer => layer.SetBounds(0, 0, 640, 360))
            .OffscreenOutput("Reusable preview", reusable, 640, 360, out _)
            .OffscreenOutput("Program", program, 1920, 1080, out _)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.OutputPass));
    }

    [Fact]
    public void Output_scene_version_binding_participates_in_canvas_cache_key()
    {
        var draftSessionId = SceneEditSessionId.New();
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("Published", scene, 1920, 1080, out _)
            .OffscreenOutput(
                "Draft",
                scene,
                1920,
                1080,
                out _,
                output => output.SceneVersionBinding = SceneVersionBinding.DraftForSession(draftSessionId))
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);

        Assert.Equal(2, graph.Count(MediaForgeRenderGraphNodeKind.CanvasRender));
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender &&
                    node.Key.Contains($"draft:{draftSessionId.Value}", StringComparison.Ordinal));
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.OutputPass &&
                    node.Key.Contains($"binding:draft:{draftSessionId.Value}", StringComparison.Ordinal));
    }

    [Fact]
    public void Output_color_space_participates_in_output_cache_key()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var scene)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(scene, source, layer => layer.SetBounds(0, 0, 1920, 1080))
            .OffscreenOutput("SDR", scene, 1920, 1080, out _, output => output.ColorSpace = RenderColorSpace.Srgb)
            .OffscreenOutput("Linear", scene, 1920, 1080, out _, output => output.ColorSpace = RenderColorSpace.Rec709Limited)
            .BuildValidated();

        var graph = MediaForgeRenderGraphCompiler.Compile(project);
        var outputKeys = graph.Nodes
            .Where(node => node.Kind == MediaForgeRenderGraphNodeKind.OutputPass)
            .Select(node => node.Key)
            .ToArray();

        Assert.Contains(outputKeys, key => key.Contains("color-space:Srgb", StringComparison.Ordinal));
        Assert.Contains(outputKeys, key => key.Contains("color-space:Rec709Limited", StringComparison.Ordinal));
        Assert.Equal(2, outputKeys.Distinct(StringComparer.Ordinal).Count());
    }
}
