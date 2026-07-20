using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsWindowCaptureSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IWindowsGraphicsCaptureSessionFactory _sessionFactory;

    public WindowsWindowCaptureSourceProviderFactory(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        GpuAdapterAffinityState? adapterAffinity = null,
        IWindowsGraphicsCaptureSessionFactory? sessionFactory = null)
    {
        _diagnostics = diagnostics;
        _sessionFactory = sessionFactory ?? new WindowsGraphicsCaptureSessionFactory(adapterAffinity);
    }

    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.WindowCapture;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        if (!CanCreate(sourceDefinition.TypeId))
        {
            throw new ArgumentException(
                $"Source type '{sourceDefinition.TypeId.Value}' is not a window capture source.",
                nameof(sourceDefinition));
        }

        var settings = (WindowCaptureSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);
        if (settings.WindowHandle == 0)
            throw new ArgumentException("Window capture requires a non-zero window handle.", nameof(sourceDefinition));

        return new WindowsWindowCaptureVideoFrameProvider(
            sourceDefinition.Id,
            sourceDefinition.Name,
            settings,
            _diagnostics,
            _sessionFactory);
    }
}
