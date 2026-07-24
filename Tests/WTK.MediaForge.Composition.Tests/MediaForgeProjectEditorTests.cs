using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaForgeProjectEditorTests
{
    [Fact]
    public void Editor_add_nested_canvas_rejects_indirect_cycle_and_rolls_back()
    {
        var editor = new MediaForgeProjectEditor(new());
        var canvasA = editor.CreateCanvas("A", new FrameSize(1920, 1080));
        var canvasB = editor.CreateCanvas("B", new FrameSize(1280, 720));

        editor.AddCanvasLayer(canvasA.Id, canvasB.Id, new Transform2D { Size = new CanvasSize(320, 240) });
        Assert.Single(canvasA.Objects);

        Assert.Throws<InvalidOperationException>(() =>
            editor.AddCanvasLayer(canvasB.Id, canvasA.Id, new Transform2D { Size = new CanvasSize(320, 240) }));

        Assert.Empty(canvasB.Objects);
        Assert.Single(canvasA.Objects);
        editor.ValidateOrThrow();
    }

    [Fact]
    public void Editor_builds_valid_minimal_project()
    {
        var editor = new MediaForgeProjectEditor(new());

        var source = editor.CreateSource("Desktop", new DesktopCaptureSourceSettings());
        var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
        editor.AddSourceLayer(
            canvas.Id,
            source.Id,
            new Transform2D
            {
                Position = new CanvasPoint(0, 0),
                Size = new CanvasSize(1920, 1080)
            });

        editor.CreateOutput(
            "Preview",
            canvas.Id,
            new PreviewWindowOutputSettings(),
            new FrameSize(1280, 720));

        editor.ValidateOrThrow();
    }

    [Fact]
    public void AddCanvasLayer_rejects_self_reference()
    {
        var editor = new MediaForgeProjectEditor(new());
        var canvas = editor.CreateCanvas("Main", new FrameSize(1920, 1080));

        Assert.Throws<InvalidOperationException>(() =>
            editor.AddCanvasLayer(canvas.Id, canvas.Id, new Transform2D { Size = new CanvasSize(100, 100) }));
    }
}
