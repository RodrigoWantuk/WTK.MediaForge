using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsWebcamSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IWindowsWebcamCaptureSessionFactory _sessionFactory;

    public WindowsWebcamSourceProviderFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IWindowsWebcamCaptureSessionFactory? sessionFactory = null)
    {
        _diagnostics = diagnostics;
        _sessionFactory = sessionFactory ?? new WindowsWebcamCaptureSessionFactory();
    }

    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.Webcam;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        var canonical = MediaSourceTypeRegistry.ResolveCanonical(sourceDefinition.TypeId);
        if (canonical != MediaSourceTypes.Webcam)
        {
            throw new ArgumentException(
                $"Source type '{sourceDefinition.TypeId.Value}' is not a webcam source.",
                nameof(sourceDefinition));
        }

        var settings = (WebcamSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);

        return new WindowsWebcamVideoFrameProvider(
            sourceDefinition.Id,
            sourceDefinition.Name,
            settings,
            _diagnostics,
            _sessionFactory);
    }
}
