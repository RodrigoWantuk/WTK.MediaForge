using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Windows.Media;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsImageSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;

    public WindowsImageSourceProviderFactory(IMediaForgeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics;

    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.ImageFile;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows static image source provider requires Windows.");

        var settings = (ImageFileSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);

        if (string.IsNullOrWhiteSpace(settings.Path))
            throw new ArgumentException("Image file path is required.", nameof(sourceDefinition));

        if (!StaticImageAssetFormats.IsSupportedExtension(settings.Path))
        {
            throw new NotSupportedException(
                $"Image format '{Path.GetExtension(settings.Path)}' is not supported. WebP is Planned until license review.");
        }

        var assetManager = new AssetManager(new WindowsStaticImageAssetDecoder());
        var runtime = new ImageFileSourceRuntime(
            sourceDefinition.Id,
            sourceDefinition.Name,
            settings,
            assetManager,
            _diagnostics);

        return new ImageFileVideoFrameProvider(sourceDefinition.Id, sourceDefinition.Name, runtime, _diagnostics);
    }
}
