using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Windows;

await using var engine = MediaForgeWindows.CreateEngine();
var firstFrame = new TaskCompletionSource<CpuReadbackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

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
    firstFrame.TrySetResult(frame);
    return ValueTask.CompletedTask;
});

await engine.AttachSinkAsync(output.Id, sink);
await engine.StartAsync();

var readback = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));

var centerX = readback.Size.Width / 2;
var centerY = readback.Size.Height / 2;
var offset = checked((int)centerY * readback.StrideBytes + (int)centerX * 4);
var pixels = readback.Pixels.ToArray();
var r = pixels[offset + 0];
var g = pixels[offset + 1];
var b = pixels[offset + 2];
var a = pixels[offset + 3];
var checksum = (long)r + g + b + a;

Console.WriteLine($"Output {output.Id} frame {readback.FrameNumber} {readback.Size} rgba=({r},{g},{b},{a}) checksum={checksum}");

if (checksum == 0)
    throw new InvalidOperationException("Sample did not receive non-zero CPU pixel data from the rendered output.");

await engine.StopAsync();
