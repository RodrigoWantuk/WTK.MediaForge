using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Windows;

await using var engine = MediaForgeWindows.CreateEngine();

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
await engine.BindOutputAsync(output.Id, new OffscreenRenderOutputTarget());
await engine.StartAsync();

await Task.Delay(TimeSpan.FromSeconds(5));

await engine.StopAsync();
