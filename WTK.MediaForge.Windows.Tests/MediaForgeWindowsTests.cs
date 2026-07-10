using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.Vulkan;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

public class MediaForgeWindowsTests
{
    [Fact]
    public async Task MediaForgeWindows_CreateEngine_returns_configured_engine()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var options = new MediaForgeEngineOptions
        {
            Diagnostics = diagnostics,
            StartTimeout = TimeSpan.FromSeconds(2),
            CommandTimeout = TimeSpan.FromSeconds(4),
            StopTimeout = TimeSpan.FromSeconds(3),
            SinkStopTimeout = TimeSpan.FromSeconds(6),
            RenderFramesPerSecond = 30
        };

        await using var engine = MediaForgeWindows.CreateEngine(options);

        Assert.Equal(MediaForgeEngineState.Idle, engine.State);
        Assert.Same(diagnostics, engine.DiagnosticsForTests);
        Assert.Equal(options.StartTimeout, engine.StartTimeout);
        Assert.Equal(options.CommandTimeout, engine.CommandTimeout);
        Assert.Equal(options.StopTimeout, engine.StopTimeout);
        Assert.Equal(options.SinkStopTimeout, engine.SinkStopTimeout);
        Assert.Equal(options.RenderFramesPerSecond, engine.RenderFramesPerSecond);
        Assert.Equal(options.StopTimeout, engine.RenderThreadJoinTimeout);
        Assert.Equal(options.StopTimeout, engine.RenderThreadSubmissionShutdownTimeout);
    }

    [Fact]
    public async Task MediaForgeWindows_CreateEngine_uses_vulkan_backend_factory()
    {
        await using var engine = MediaForgeWindows.CreateEngine();

        Assert.IsType<MediaForgeVulkanRenderBackendFactory>(engine.BackendFactoryForTests);
    }

    [Fact]
    public async Task MediaForgeWindows_CreateEngine_does_not_require_manual_runtime_wiring()
    {
        await using var engine = MediaForgeWindows.CreateEngine();

        Assert.True(engine.SourceProviderFactoryForTests.CanCreate(MediaSourceTypes.Desktop));
        Assert.True(engine.SourceProviderFactoryForTests.CanCreate(MediaSourceTypes.ImageFile));
        Assert.True(engine.OutputSinkFactoryForTests.CanCreate(RenderOutputTypes.Offscreen));
    }

    [Fact]
    public void MediaForgeWindows_CreateEngine_rejects_invalid_options()
    {
        var options = new MediaForgeEngineOptions
        {
            StartTimeout = TimeSpan.Zero
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => MediaForgeWindows.CreateEngine(options));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaForgeWindows.CreateEngine(new MediaForgeEngineOptions { CommandTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaForgeWindows.CreateEngine(new MediaForgeEngineOptions { StopTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaForgeWindows.CreateEngine(new MediaForgeEngineOptions { SinkStopTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaForgeWindows.CreateEngine(new MediaForgeEngineOptions { RenderFramesPerSecond = 0 }));
    }
}
