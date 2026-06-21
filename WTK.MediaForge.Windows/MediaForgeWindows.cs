using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Graphics.Vulkan;

namespace WTK.MediaForge.Windows;

public static class MediaForgeWindows
{
    public static MediaForgeEngine CreateEngine(MediaForgeEngineOptions? options = null)
    {
        options ??= new MediaForgeEngineOptions();
        ValidateOptions(options);

        return new MediaForgeEngine(
            new WindowsDesktopSourceProviderFactory(options.Diagnostics),
            new WindowsRenderOutputSinkFactory(),
            new MediaForgeVulkanRenderBackendFactory(),
            options.Diagnostics)
        {
            RenderThreadJoinTimeout = options.StopTimeout,
            RenderThreadSubmissionShutdownTimeout = options.StopTimeout
        };
    }

    private static void ValidateOptions(MediaForgeEngineOptions options)
    {
        if (options.StartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StartTimeout must be positive.");

        if (options.StopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StopTimeout must be positive.");
    }
}
