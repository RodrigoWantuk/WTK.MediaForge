using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Graphics.Vulkan;
using WTK.MediaForge.Windows.Media;
using WTK.MediaForge.Windows.Media.Text;

namespace WTK.MediaForge.Windows;

public static class MediaForgeWindows
{
    private static readonly IHardwareMediaCapabilityProbe DefaultCapabilityProbe =
        new WindowsHardwareMediaCapabilityProbe();

    public static MediaForgeEngine CreateEngine(MediaForgeEngineOptions? options = null)
    {
        options ??= new MediaForgeEngineOptions();
        ValidateOptions(options);

        return new MediaForgeEngine(
            new CompositeMediaSourceProviderFactory(
            new WindowsDesktopSourceProviderFactory(options.Diagnostics),
            new WindowsImageSourceProviderFactory(options.Diagnostics),
            new WindowsUnavailableLiveSourceProviderFactory(options.Diagnostics),
            new WindowsVideoFileSourceProviderFactory(options.Diagnostics)),
            new WindowsRenderOutputSinkFactory(),
            new MediaForgeVulkanRenderBackendFactory(new WindowsSystemDrawingFontAtlasRasterizer()),
            options.Diagnostics)
        {
            StartTimeout = options.StartTimeout,
            CommandTimeout = options.CommandTimeout,
            StopTimeout = options.StopTimeout,
            SinkStopTimeout = options.SinkStopTimeout,
            RenderFramesPerSecond = options.RenderFramesPerSecond,
            RenderThreadJoinTimeout = options.StopTimeout,
            RenderThreadSubmissionShutdownTimeout = options.StopTimeout
        };
    }

    public static ValueTask<MediaForgeCapabilityReport> GetCapabilityReportAsync(
        CancellationToken cancellationToken = default) =>
        GetCapabilityReportAsync(DefaultCapabilityProbe, cancellationToken);

    public static async ValueTask<MediaForgeCapabilityReport> GetCapabilityReportAsync(
        IHardwareMediaCapabilityProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var hardware = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return MediaForgeCapabilityReportBuilder.Build(
            hardware,
            MediaSourceTypeRegistry.CreateCapabilityEntries()
                .Concat(RenderOutputTypeRegistry.CreateCapabilityEntries()));
    }

    private static void ValidateOptions(MediaForgeEngineOptions options)
    {
        if (options.StartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StartTimeout must be positive.");

        if (options.CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "CommandTimeout must be positive.");

        if (options.StopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StopTimeout must be positive.");

        if (options.SinkStopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "SinkStopTimeout must be positive.");

        if (!double.IsFinite(options.RenderFramesPerSecond) || options.RenderFramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RenderFramesPerSecond must be finite and positive.");
    }
}
