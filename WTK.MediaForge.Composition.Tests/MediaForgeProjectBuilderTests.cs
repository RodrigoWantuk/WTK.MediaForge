using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaForgeProjectBuilderTests
{
    [Fact]
    public void ProjectBuilder_creates_valid_desktop_to_offscreen_project()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop)
            .AddSourceLayer(main, desktop, layer =>
                layer.SetBounds(0, 0, 1920, 1080).SetFit())
            .OffscreenOutput("Program", main, 1920, 1080, out _)
            .BuildValidated();

        Assert.True(MediaForgeProjectValidator.Validate(project).IsValid);
        Assert.Single(project.SourceDefinitions);
        Assert.Single(project.Canvases);
        Assert.Single(project.Outputs);
    }

    [Fact]
    public void ProjectBuilder_does_not_expose_JsonObject_for_normal_source_settings()
    {
        var desktopSourceMethods = typeof(MediaForgeProjectBuilder)
            .GetMethods()
            .Where(method => method.Name == nameof(MediaForgeProjectBuilder.DesktopSource));

        Assert.DoesNotContain(
            desktopSourceMethods.SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(JsonObject));
    }

    [Fact]
    public void ProjectBuilder_add_source_layer_preserves_bounds_and_layout()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop)
            .AddSourceLayer(main, desktop, layer =>
                layer.SetBounds(10, 20, 640, 360).SetFill().SetOpacity(0.5f))
            .OffscreenOutput("Program", main, 1920, 1080, out _)
            .BuildValidated();

        var layer = Assert.IsType<SourceLayerDrawObject>(project.Canvases[0].Objects[0]);
        Assert.Equal(LayoutMode.Fill, layer.LayoutMode);
        Assert.Equal(10, layer.Transform.Position.X);
        Assert.Equal(20, layer.Transform.Position.Y);
        Assert.Equal(640, layer.Transform.Size.Width);
        Assert.Equal(360, layer.Transform.Size.Height);
        Assert.Equal(0.5f, layer.Opacity);
    }

    [Fact]
    public void ProjectBuilder_build_validated_throws_validation_exception()
    {
        var invalidProject = new MediaForgeProject
        {
            Canvases =
            [
                new()
                {
                    Name = "Invalid",
                    Size = new FrameSize(0, 0)
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            MediaForgeProjectBuilder.FromProject(invalidProject).BuildValidated());
    }
}
