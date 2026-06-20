using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class MediaForgeEngineTests
{
    [Fact]
    public async Task LoadProjectAsync_rejects_invalid_project()
    {
        await using var engine = new MediaForgeEngine();
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new()
                {
                    Name = "Empty size",
                    Size = default
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.LoadProjectAsync(project));
    }

    [Fact]
    public async Task ApplyProjectUpdateAsync_runs_editor_validation()
    {
        await using var engine = new MediaForgeEngine();

        var editor = new MediaForgeProjectEditor(new());
        var source = editor.CreateSource("Desktop", new DesktopCaptureSourceSettings());
        var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
        editor.AddSourceLayer(
            canvas.Id,
            source.Id,
            new Transform2D { Size = new CanvasSize(1920, 1080) });
        editor.CreateOutput("Preview", canvas.Id, new PreviewWindowOutputSettings(), new FrameSize(1280, 720));

        await engine.LoadProjectAsync(editor.Project);

        await engine.ApplyProjectUpdateAsync(e =>
        {
            e.AddText(canvas.Id, "Live", new Transform2D { Size = new CanvasSize(200, 64) });
        });

        Assert.Contains(
            engine.CurrentProject.Canvases[0].Objects,
            o => o.Name == "Text");
    }

    [Fact]
    public async Task BindOutputAsync_requires_matching_type()
    {
        await using var engine = new MediaForgeEngine();
        var editor = new MediaForgeProjectEditor(new());
        var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
        var output = editor.CreateOutput(
            "Preview",
            canvas.Id,
            new PreviewWindowOutputSettings(),
            new FrameSize(1280, 720));
        editor.ValidateOrThrow();

        await engine.LoadProjectAsync(editor.Project);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.BindOutputAsync(output.Id, new OffscreenRenderOutputTarget()));
    }
}
