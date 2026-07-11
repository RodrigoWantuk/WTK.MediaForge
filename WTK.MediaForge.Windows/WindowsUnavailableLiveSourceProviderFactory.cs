using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsUnavailableLiveSourceProviderFactory(
    IMediaForgeDiagnosticsSink? diagnostics = null) : IMediaSourceProviderFactory
{
    public bool CanCreate(MediaSourceTypeId typeId)
    {
        var canonical = MediaSourceTypeRegistry.ResolveCanonical(typeId);
        return canonical == MediaSourceTypes.WindowCapture ||
               canonical == MediaSourceTypes.Webcam;
    }

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        var canonical = MediaSourceTypeRegistry.ResolveCanonical(sourceDefinition.TypeId);
        var message = canonical == MediaSourceTypes.WindowCapture
            ? "Windows window capture is planned until a Windows Graphics Capture provider publishes D3D11 GPU frame leases."
            : canonical == MediaSourceTypes.Webcam
                ? "Windows webcam capture is planned until a provider uploads system frames to GPU immediately and is product validated."
                : $"Source type '{sourceDefinition.TypeId.Value}' is not handled by this provider factory.";

        var featureCode = $"source.{canonical.Value}";
        var exception = new MediaForgeUnsupportedFeatureException(featureCode, message);

        MediaForgeDiagnostics.Report(
            diagnostics,
            MediaForgeDiagnosticSeverity.Error,
            "source.provider_unavailable",
            message,
            nameof(WindowsUnavailableLiveSourceProviderFactory),
            exception,
            sourceDefinition.Id.Value,
            sourceDefinition.Name);

        throw exception;
    }
}
