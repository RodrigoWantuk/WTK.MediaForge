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

    [Theory]
    [InlineData("source")]
    [InlineData("text")]
    [InlineData("solid")]
    [InlineData("nested")]
    [InlineData("adjustment")]
    [InlineData("output")]
    public void ProjectBuilder_rolls_back_created_items_when_configure_throws(string operation)
    {
        var project = CreateRollbackProject(out var main, out var nested, out var source);
        var before = MediaForgeProjectSerializer.Serialize(project);
        var builder = MediaForgeProjectBuilder.FromProject(project);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "source":
                    builder.AddSourceLayer(main, source, _ => throw new InvalidOperationException("boom"));
                    break;
                case "text":
                    builder.AddText(main, "Title", _ => throw new InvalidOperationException("boom"));
                    break;
                case "solid":
                    builder.AddSolid(main, ColorRgba.Black, _ => throw new InvalidOperationException("boom"));
                    break;
                case "nested":
                    builder.AddCanvasLayer(main, nested, _ => throw new InvalidOperationException("boom"));
                    break;
                case "adjustment":
                    builder.AddAdjustmentLayer(main, _ => throw new InvalidOperationException("boom"));
                    break;
                case "output":
                    builder.OffscreenOutput("Program", main, 1920, 1080, out _, _ => throw new InvalidOperationException("boom"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation.");
            }
        });

        Assert.Equal("boom", exception.Message);
        Assert.Equal(before, MediaForgeProjectSerializer.Serialize(builder.Build()));
        Assert.Empty(main.Objects);
    }

    [Fact]
    public void ProjectBuilder_rolls_back_against_internal_canvas_when_external_canvas_has_same_id()
    {
        var project = CreateRollbackProject(out var main, out _, out _);
        var before = MediaForgeProjectSerializer.Serialize(project);
        var externalCanvas = MediaForgeProjectSerializer.Deserialize(before).Canvases.Single(canvas => canvas.Id == main.Id);
        var builder = MediaForgeProjectBuilder.FromProject(project);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddSolid(
                externalCanvas,
                ColorRgba.White,
                layer =>
                {
                    layer.SetName("Should roll back");
                    throw new InvalidOperationException("boom");
                }));

        Assert.Empty(externalCanvas.Objects);
        Assert.Equal(before, MediaForgeProjectSerializer.Serialize(builder.Build()));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 1f, 1f)]
    [InlineData(0f, float.PositiveInfinity, 1f, 1f)]
    [InlineData(0.8f, 0f, 0.2f, 1f)]
    [InlineData(-0.1f, 0f, 1f, 1f)]
    [InlineData(0f, 0f, 1.1f, 1f)]
    public void LayerBuilder_set_crop_rejects_invalid_normalized_rect(float left, float top, float right, float bottom)
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSolid(
                main,
                ColorRgba.Black,
                layer => layer.SetCrop(left, top, right, bottom)));
    }

    [Fact]
    public void LayerBuilder_clear_crop_removes_existing_crop()
    {
        var project = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .AddSolid(
                main,
                ColorRgba.Black,
                layer => layer.SetCrop(0.1f, 0.1f, 0.9f, 0.9f).ClearCrop())
            .BuildValidated();

        var layer = Assert.IsType<SolidDrawObject>(Assert.Single(project.Canvases[0].Objects));
        Assert.Null(layer.Crop);
    }

    [Theory]
    [InlineData(float.NaN, 0.5f)]
    [InlineData(0.5f, float.PositiveInfinity)]
    [InlineData(-0.1f, 0.5f)]
    [InlineData(0.5f, 1.1f)]
    public void LayerBuilder_set_pivot_rejects_invalid_unit_values(float x, float y)
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddText(
                main,
                "Title",
                layer => layer.SetPivot(x, y)));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void LayerBuilder_set_rotation_rejects_non_finite_values(float rotationDegrees)
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSolid(
                main,
                ColorRgba.Black,
                layer => layer.SetRotationDegrees(rotationDegrees)));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void LayerBuilder_set_opacity_rejects_invalid_values(float opacity)
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSolid(
                main,
                ColorRgba.Black,
                layer => layer.SetOpacity(opacity)));
    }

    [Fact]
    public void LayerBuilder_color_helpers_reject_invalid_colors_consistently()
    {
        var invalid = new ColorRgba(float.NaN, 0f, 0f, 1f);
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main)
            .DesktopSource("Desktop", displayIndex: 0, out var desktop);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSolid(main, ColorRgba.Black, layer => layer.SetFillColor(invalid)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddText(main, "Title", layer => layer.SetTextColor(invalid)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSourceLayer(main, desktop, layer => layer.SetLetterboxColor(invalid)));
    }

    [Fact]
    public void LayerBuilder_set_blend_mode_rejects_invalid_enum_values()
    {
        var invalid = (BlendMode)42;
        var builder = MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out var main);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSolid(
                main,
                ColorRgba.Black,
                layer => layer.SetBlendMode(invalid)));
    }

    private static MediaForgeProject CreateRollbackProject(
        out MediaForgeCanvas main,
        out MediaForgeCanvas nested,
        out MediaForgeSourceDefinition source) =>
        MediaForgeProjectBuilder.Create()
            .Canvas("Main", 1920, 1080, out main)
            .Canvas("Nested", 640, 360, out nested)
            .DesktopSource("Desktop", displayIndex: 0, out source)
            .BuildValidated();
}
