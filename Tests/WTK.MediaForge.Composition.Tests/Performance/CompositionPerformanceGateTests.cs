using System.Diagnostics;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;
using PublicRenderOutputSink = WTK.MediaForge.Composition.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Composition.Tests.Performance;

[Trait("Category", "Performance")]
public sealed class CompositionPerformanceGateTests
{
    [Fact]
    public async Task Composition_performance_tier_contains_real_tests()
    {
        var output = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Program",
            TypeId = RenderOutputTypes.Offscreen,
            CanvasId = CanvasId.New(),
            OutputSize = new FrameSize(1920, 1080)
        };
        var firstSink = new CountingSink();
        var secondSink = new CountingSink();
        await using var dispatcher = new RenderOutputSinkDispatcher(
            sinkStopTimeout: TimeSpan.FromSeconds(1));

        await dispatcher.AttachAsync(output, firstSink, TimeSpan.FromSeconds(1), CancellationToken.None);
        await dispatcher.AttachAsync(output, secondSink, TimeSpan.FromSeconds(1), CancellationToken.None);

        var surfaces = new List<CountingSurfaceLease>();
        var stopwatch = Stopwatch.StartNew();

        for (var frame = 0; frame < 120; frame++)
        {
            var surface = new CountingSurfaceLease(output.Id, output.OutputSize);
            surfaces.Add(surface);
            var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);

            dispatcher.PublishCompletedFrames(batch);
            await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        }

        stopwatch.Stop();

        Assert.Equal(120, firstSink.FrameCount);
        Assert.Equal(120, secondSink.FrameCount);
        Assert.All(surfaces, surface => Assert.Equal(1, surface.DisposeCount));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    private sealed class CountingSink : PublicRenderOutputSink
    {
        private int _frameCount;

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public int FrameCount => Volatile.Read(ref _frameCount);

        public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _frameCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingSurfaceLease(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease
    {
        private int _disposeCount;

        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = new object();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
