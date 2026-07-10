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
    private readonly Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder> _decoderFactory;

    public WindowsVideoFileSourceProviderFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        bool enablePrototypeProvider = false,
        Func<HardwareDecodeOpenContext, IHardwareFileVideoDecoder>? decoderFactory = null)
    {
        _diagnostics = diagnostics;
        _enablePrototypeProvider = enablePrototypeProvider;
        _decoderFactory = decoderFactory ?? CreatePrototypeDecoder;
    }

    public bool CanCreate(MediaSourceTypeId typeId) =>
        _enablePrototypeProvider &&
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

        if (!_enablePrototypeProvider)
        {
            throw new MediaForgeUnsupportedFeatureException(
                $"source.{MediaSourceTypes.VideoFile.Value}",
                "Windows video file source provider is prototype-only until real hardware decode is product validated.");
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
}
