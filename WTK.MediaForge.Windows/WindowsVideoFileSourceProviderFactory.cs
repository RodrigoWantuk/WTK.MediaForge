using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Windows.Media.Decode;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsVideoFileSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly bool _enablePrototypeProvider;
    private readonly bool _enableProductProvider;
    private readonly Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder> _decoderFactory;

    public WindowsVideoFileSourceProviderFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        bool enablePrototypeProvider = false,
        bool enableProductProvider = true,
        Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder>? decoderFactory = null)
    {
        _diagnostics = diagnostics;
        _enablePrototypeProvider = enablePrototypeProvider;
        _enableProductProvider = enableProductProvider;
        _decoderFactory = decoderFactory ??
                          (enablePrototypeProvider
                              ? CreatePrototypeDecoder
                              : CreateProductDecoder);
    }

    public bool CanCreate(MediaSourceTypeId typeId) =>
        (_enableProductProvider || _enablePrototypeProvider) &&
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.VideoFile;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        var canonical = MediaSourceTypeRegistry.ResolveCanonical(sourceDefinition.TypeId);
        if (canonical != MediaSourceTypes.VideoFile)
        {
            throw new ArgumentException(
                $"Source type '{sourceDefinition.TypeId.Value}' is not a video file source.",
                nameof(sourceDefinition));
        }

        if (!_enableProductProvider && !_enablePrototypeProvider)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"source.{MediaSourceTypes.VideoFile.Value}",
                "Windows video file source provider is unavailable because neither the product hardware decoder nor the explicit internal prototype decoder is enabled.");
        }

        var settings = (VideoFileSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);

        if (string.IsNullOrWhiteSpace(settings.Path))
            throw new ArgumentException("Video file path is required.", nameof(sourceDefinition));

        var runtime = new VideoSourceRuntime(settings, _decoderFactory, _diagnostics);
        return new WindowsVideoFileVideoFrameProvider(
            sourceDefinition.Id,
            sourceDefinition.Name,
            runtime,
            _diagnostics);
    }

    private static IHardwareFileVideoDecoder CreatePrototypeDecoder(HardwareDecodeOpenContext context)
    {
        _ = context;
        return new MediaFoundationHardwareVideoDecoder(allowPrototypeDecoding: true);
    }

    private static IHardwareFileVideoDecoder CreateProductDecoder(HardwareDecodeOpenContext context)
    {
        _ = context;
        return new MediaFoundationHardwareVideoDecoder();
    }
}
