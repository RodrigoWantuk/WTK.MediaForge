using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Windows;

await using var engine = MediaForgeWindows.CreateEngine();
var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

var project = MediaForgeProjectBuilder.Create()
    .Canvas("Main", 1920, 1080, out var main)
    .DesktopSource("Desktop", displayIndex: 0, out var desktop)
    .AddSourceLayer(
        main,
        desktop,
        layer => layer.SetBounds(0, 0, 1920, 1080).SetFit())
    .OffscreenOutput("Program", main, 1920, 1080, out var output)
    .BuildValidated();

await engine.LoadProjectAsync(project);

var sink = new CpuReadbackSink(onFrame: (frame, _) =>
{
    Console.WriteLine($"Output {frame.OutputId} frame {frame.FrameNumber} {frame.Size}");
    firstFrame.TrySetResult();
    return ValueTask.CompletedTask;
});

await engine.AttachSinkAsync(output.Id, sink);
await engine.StartAsync();

await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));

await engine.StopAsync();
