using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Effects.Graph;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects;

internal sealed class VulkanEffectChainExecutor : IDisposable
{
    private readonly VulkanEffectGraphExecutor _effectGraph = VulkanEffectGraphExecutorFactory.CreateDefault();
    private bool _disposed;

    public VulkanColorCorrectionPass ColorCorrection { get; } = new();

    public VulkanSeparableBlurPass Blur { get; } = new();

    public bool HasActiveEffects =>
        ColorCorrection.IsEnabled || Blur.IsEnabled;

    public void ExecuteSkeleton(
        VulkanHeadlessDevice device,
        RenderDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(drawObject);

        using var pool = new VulkanGpuResourcePool(device);
        var context = new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = pool,
            Input = new EffectPassDescriptor { Size = new Core.Frames.FrameSize(1, 1) },
            Output = new EffectPassDescriptor { Size = new Core.Frames.FrameSize(1, 1) },
            DrawObject = drawObject
        };

        _effectGraph.ExecuteChain(context, drawObject);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _effectGraph.Dispose();
    }
}
