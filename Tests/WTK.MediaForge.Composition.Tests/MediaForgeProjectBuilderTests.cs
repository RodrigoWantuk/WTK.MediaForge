using System.Text.Json.Nodes;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
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
    public void ProjectBuilder_add_color_correction_creates_layer_effect()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop)
            .AddSourceLayer(main, desktop, layer =>
                layer.AddColorCorrection(
                    brightness: 0.1f,
                    contrast: 1.2f,
                    saturation: 0.8f,
                    hueDegrees: -15f))
            .OffscreenOutput("Program", main, 1920, 1080, out _)
            .BuildValidated();

        var layer = Assert.IsType<SourceLayerDrawObject>(project.Canvases[0].Objects[0]);
        var effect = Assert.IsType<ColorCorrectionEffect>(Assert.Single(layer.Effects));
        Assert.Equal(0.1f, effect.Brightness);
        Assert.Equal(1.2f, effect.Contrast);
        Assert.Equal(0.8f, effect.Saturation);
        Assert.Equal(-15f, effect.HueDegrees);
    }

    [Fact]
    public void ProjectBuilder_add_solid_creates_configured_solid_layer()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .AddSolid(
                main,
                ColorRgba.From(0.1f, 0.2f, 0.3f, 1f),
                layer => layer
                    .SetName("Background")
                    .SetBounds(0, 0, 1920, 1080)
                    .SetRotationDegrees(2.5f)
                    .SetPivot(0.5f, 0.5f)
                    .SetCrop(0.1f, 0.2f, 0.9f, 0.95f)
                    .SetOpacity(0.8f)
                    .SetBlendMode(BlendMode.Add))
            .OffscreenOutput("Program", main, 1920, 1080, out _)
            .BuildValidated();

        var layer = Assert.IsType<SolidDrawObject>(project.Canvases[0].Objects[0]);
        Assert.Equal("Background", layer.Name);
        Assert.Equal(ColorRgba.From(0.1f, 0.2f, 0.3f, 1f), layer.FillColor);
        Assert.Equal(1920, layer.Transform.Size.Width);
        Assert.Equal(1080, layer.Transform.Size.Height);
        Assert.Equal(2.5f, layer.Transform.RotationDegrees);
        Assert.Equal(new NormalizedPoint(0.5f, 0.5f), layer.Transform.Pivot);
        Assert.Equal(new NormalizedRect(0.1f, 0.2f, 0.9f, 0.95f), layer.Crop);
        Assert.Equal(0.8f, layer.Opacity);
        Assert.Equal(BlendMode.Add, layer.BlendMode);
    }

    [Fact]
    public void ProjectBuilder_layer_helpers_cover_transform_crop_visibility_and_blend()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Nested", 640, 360, out var nested)
            .AddText(
                nested,
                "Nested",
                layer => layer
                    .SetBounds(10, 20, 300, 80)
                    .SetRotationDegrees(15)
                    .SetPivot(0.5f, 0.5f)
                    .SetCrop(0, 0, 0.75f, 1)
                    .SetEnabled(false)
                    .SetBlendMode(BlendMode.Add))
            .Canvas("Main", 1920, 1080, out var main)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop)
            .AddSourceLayer(
                main,
                desktop,
                layer => layer
                    .SetBounds(0, 0, 960, 540)
                    .SetRotationDegrees(-5)
                    .SetPivot(0.25f, 0.75f)
                    .SetCrop(0.05f, 0.05f, 0.95f, 0.95f)
                    .SetEnabled(true)
                    .SetBlendMode(BlendMode.Add))
            .AddCanvasLayer(
                main,
                nested,
                layer => layer
                    .SetBounds(960, 540, 640, 360)
                    .SetRotationDegrees(5)
                    .SetPivot(1, 1)
                    .SetCrop(0.1f, 0.1f, 1, 1)
                    .SetEnabled(false)
                    .SetBlendMode(BlendMode.Add))
            .OffscreenOutput("Program", main, 1920, 1080, out _)
            .BuildValidated();

        var text = Assert.IsType<TextDrawObject>(project.Canvases[0].Objects[0]);
        Assert.False(text.Enabled);
        Assert.Equal(BlendMode.Add, text.BlendMode);
        Assert.Equal(new NormalizedRect(0, 0, 0.75f, 1), text.Crop);
        Assert.Equal(15, text.Transform.RotationDegrees);
        Assert.Equal(new NormalizedPoint(0.5f, 0.5f), text.Transform.Pivot);

        var source = Assert.IsType<SourceLayerDrawObject>(project.Canvases[1].Objects[0]);
        Assert.True(source.Enabled);
        Assert.Equal(BlendMode.Add, source.BlendMode);
        Assert.Equal(new NormalizedRect(0.05f, 0.05f, 0.95f, 0.95f), source.Crop);
        Assert.Equal(-5, source.Transform.RotationDegrees);
        Assert.Equal(new NormalizedPoint(0.25f, 0.75f), source.Transform.Pivot);

        var canvas = Assert.IsType<CanvasDrawObject>(project.Canvases[1].Objects[1]);
        Assert.False(canvas.Enabled);
        Assert.Equal(BlendMode.Add, canvas.BlendMode);
        Assert.Equal(new NormalizedRect(0.1f, 0.1f, 1, 1), canvas.Crop);
        Assert.Equal(5, canvas.Transform.RotationDegrees);
        Assert.Equal(new NormalizedPoint(1, 1), canvas.Transform.Pivot);
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

        Assert.Throws<MediaForgeProjectValidationException>(() =>
            MediaForgeProjectBuilder.FromProject(invalidProject).BuildValidated());
    }
}
