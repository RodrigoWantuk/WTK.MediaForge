using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Editor;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Tests.Engine;

internal static class EngineLifecycleTestSupport
{
    public static MediaForgeEngine CreateEngine(
        IMediaSourceProviderFactory? providerFactory = null,
        IRenderBackendFactory? backendFactory = null,
        IRenderOutputSinkFactory? outputSinkFactory = null,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IEncodedOutputRouteFactory? encodedOutputRouteFactory = null) =>
        new(
            providerFactory ?? new FakeMediaSourceProviderFactory(),
            outputSinkFactory ?? new FakeRenderOutputSinkFactory(),
            backendFactory ?? new RecordingRenderBackendFactory(),
            diagnostics,
            encodedOutputRouteFactory);

    public static WinFormsPreviewRenderOutputTarget CreatePreviewTarget(nint handle) =>
        new(handle);

    public static MediaForgeProject CreateValidProject()
    {
        var editor = new MediaForgeProjectEditor(new());
        var source = editor.CreateSource("Desktop", new DesktopCaptureSourceSettings());
        var canvas = editor.CreateCanvas("Program", new FrameSize(1920, 1080));
        editor.AddSourceLayer(
            canvas.Id,
            source.Id,
            new Transform2D { Size = new CanvasSize(1920, 1080) });
        editor.CreateOutput(
            "Preview",
            canvas.Id,
            new PreviewWindowOutputSettings(),
            new FrameSize(1280, 720));
        editor.ValidateOrThrow();
        return editor.Project;
    }

    public static MediaForgeProject CreateOffscreenProject() =>
        MediaForgeProjectBuilder.Create()
            .Canvas("Program", 1920, 1080, out var canvas)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(
                canvas,
                source,
                layer => layer.SetBounds(0, 0, 1920, 1080).SetFit())
            .OffscreenOutput("Program", canvas, 1920, 1080, out _)
            .BuildValidated();

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        if (condition())
            return;

        throw new TimeoutException("Condition was not met within the expected timeout.");
    }
}
