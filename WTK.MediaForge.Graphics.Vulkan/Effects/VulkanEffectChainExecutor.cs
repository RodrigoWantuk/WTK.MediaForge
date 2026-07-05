using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Effects;

internal sealed class VulkanEffectChainExecutor : IDisposable
{
    private readonly VulkanColorCorrectionPass _colorCorrection = new();
    private readonly VulkanSeparableBlurPass _blur = new();
    private bool _disposed;

    public VulkanColorCorrectionPass ColorCorrection => _colorCorrection;

    public VulkanSeparableBlurPass Blur => _blur;

    public bool HasActiveEffects =>
        _colorCorrection.IsEnabled || _blur.IsEnabled;

    public void ExecuteSkeleton(
        VulkanHeadlessDevice device,
        RenderDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(drawObject);

        if (_colorCorrection.CanApply(drawObject))
            _colorCorrection.ApplySkeleton(device);

        if (_blur.CanApply(drawObject))
        {
            _blur.ApplyHorizontalSkeleton(device);
            _blur.ApplyVerticalSkeleton(device);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
