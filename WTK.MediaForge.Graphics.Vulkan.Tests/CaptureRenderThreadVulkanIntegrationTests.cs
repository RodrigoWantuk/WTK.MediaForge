using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.Vulkan;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Collection("GpuCapture")]
[Trait("Category", TestCategories.Gpu)]
public class CaptureRenderThreadVulkanIntegrationTests
{
    [Fact]
    public async Task Capture_render_thread_vulkan_factory_submits_without_fault()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var canvasId = CanvasId.New();
        var outputId = RenderOutputId.New();
        var outputSize = new FrameSize(1280, 720);
        await using var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var lease))
                    return false;

                lease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        using var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(provider);

        var guard = new RenderThreadGuard();
        var factory = new MediaForgeVulkanRenderBackendFactory();
        if (!factory.TryCreate(guard, diagnostics: null, out var backend) || backend is null)
            return;

        using var renderThread = new MediaForgeRenderThread(backend, guard, maxFramesInFlight: 2);
        renderThread.Start();

        await renderThread.EnqueueCommandAsync(new BindOutputCommand
        {
            Binding = new RenderOutputBindingSnapshot
            {
                OutputId = outputId,
                TargetKind = RenderTargetKind.Offscreen,
                SurfaceSize = outputSize,
                BindingVersion = 1
            }
        });

        var projectState = CreateProjectState(sourceId, canvasId, outputId, outputSize);
        using var buildResult = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = buildResult.TakeSnapshot();
        Assert.NotNull(snapshot);
        renderThread.PublishFrame(snapshot!);

        var vulkanBackend = (Rendering.MediaForgeVulkanRenderer)backend;
        await WaitUntilAsync(() => vulkanBackend.SubmitCount >= 1, TimeSpan.FromSeconds(10));

        renderThread.Dispose();
        await provider.StopAsync(CancellationToken.None);
    }

    private static ProjectStateSnapshot CreateProjectState(
        SourceId sourceId,
        CanvasId canvasId,
        RenderOutputId outputId,
        FrameSize outputSize) =>
        new()
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = canvasId,
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Desktop",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(1920, 1080) }
                        }
                    ]
                }
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Program",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = outputSize
                }
            ]
        };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        if (condition())
            return;

        throw new TimeoutException("Condition was not met within the expected timeout.");
    }
}
