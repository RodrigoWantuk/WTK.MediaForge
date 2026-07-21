using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scene;

public sealed class VisualStateIdentityTests
{
    public static TheoryData<string> VisualMutations =>
    [
        "layer-order",
        "blend-mode",
        "layout-mode",
        "letterbox",
        "content-rotation",
        "text",
        "text-color",
        "font-family",
        "solid-color",
        "nested-binding",
        "background"
    ];

    [Theory]
    [MemberData(nameof(VisualMutations))]
    public void Visual_mutation_changes_version_dirty_state_and_render_graph(string mutation)
    {
        var project = CreateProject();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(ProjectStateSnapshotFactory.CreateImmutableSnapshot(project));
        var canvasId = project.Canvases[0].Id;
        var initialVersion = runtime.GetPublishedVersion(canvasId);
        var initialPlan = runtime.CreateSnapshot().CachedRenderGraphPlan!;

        ApplyMutation(project, mutation);
        runtime.SyncFrom(ProjectStateSnapshotFactory.CreateImmutableSnapshot(project));
        var updated = runtime.CreateSnapshot();

        Assert.NotEqual(initialVersion, runtime.GetPublishedVersion(canvasId));
        Assert.True(updated.DirtyRegion.RequiresGraphRecompile);
        Assert.NotSame(initialPlan, updated.CachedRenderGraphPlan);
        Assert.NotEqual(
            initialPlan.Nodes.Select(static node => node.Key).Order().ToArray(),
            updated.CachedRenderGraphPlan!.Nodes.Select(static node => node.Key).Order().ToArray());
    }

    [Fact]
    public void Metadata_only_change_preserves_version_dirty_state_and_render_graph()
    {
        var project = CreateProject();
        var runtime = new SceneRuntime();
        runtime.SyncFrom(ProjectStateSnapshotFactory.CreateImmutableSnapshot(project));
        var canvasId = project.Canvases[0].Id;
        var initialVersion = runtime.GetPublishedVersion(canvasId);
        var initialPlan = runtime.CreateSnapshot().CachedRenderGraphPlan!;

        project.Canvases[0].Name = "Renamed scene metadata";
        project.Canvases[0].Objects[0].Name = "Renamed layer metadata";
        runtime.SyncFrom(ProjectStateSnapshotFactory.CreateImmutableSnapshot(project));
        var updated = runtime.CreateSnapshot();

        Assert.Equal(initialVersion, runtime.GetPublishedVersion(canvasId));
        Assert.False(updated.DirtyRegion.RequiresGraphRecompile);
        Assert.Empty(updated.DirtyRegion.LayerDirtyKinds);
        Assert.Same(initialPlan, updated.CachedRenderGraphPlan);
    }

    private static MediaForgeProject CreateProject()
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var program)
            .Scene("Nested", 640, 360, out var nested)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(program, source)
            .AddText(program, "Original")
            .AddCanvasLayer(program, nested)
            .OffscreenOutput("Program", program, 1920, 1080, out _);
        var project = builder.BuildValidated();
        project.Canvases[0].Objects.Add(new SolidDrawObject { Name = "Solid", FillColor = ColorRgba.White });
        return project;
    }

    private static void ApplyMutation(MediaForgeProject project, string mutation)
    {
        var canvas = project.Canvases[0];
        var source = Assert.IsType<SourceLayerDrawObject>(canvas.Objects[0]);
        var text = Assert.IsType<TextDrawObject>(canvas.Objects[1]);
        var nested = Assert.IsType<CanvasDrawObject>(canvas.Objects[2]);
        var solid = Assert.IsType<SolidDrawObject>(canvas.Objects[3]);

        switch (mutation)
        {
            case "layer-order":
                (canvas.Objects[0], canvas.Objects[1]) = (canvas.Objects[1], canvas.Objects[0]);
                break;
            case "blend-mode":
                source.BlendMode = BlendMode.Add;
                break;
            case "layout-mode":
                source.LayoutMode = LayoutMode.Fill;
                break;
            case "letterbox":
                source.LetterboxColor = ColorRgba.From(0.2f, 0.3f, 0.4f, 0.5f);
                break;
            case "content-rotation":
                source.ContentRotationOverride = DisplayRotation.Rotate90;
                break;
            case "text":
                text.Text = "Updated";
                break;
            case "text-color":
                text.TextColor = ColorRgba.From(0.9f, 0.1f, 0.2f, 0.8f);
                break;
            case "font-family":
                text.FontFamily = "Noto Sans";
                break;
            case "solid-color":
                solid.FillColor = ColorRgba.From(0.1f, 0.8f, 0.2f, 0.4f);
                break;
            case "nested-binding":
                nested.VersionBinding = SceneVersionBinding.ExplicitVersion(SceneVersionId.New());
                break;
            case "background":
                canvas.BackgroundColor = ColorRgba.From(0.1f, 0.2f, 0.3f, 1f);
                break;
            default:
                throw new InvalidOperationException($"Unsupported test mutation '{mutation}'.");
        }
    }
}
