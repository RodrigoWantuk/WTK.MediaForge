using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsDesktopSourceProviderFactory(IMediaForgeDiagnosticsSink? diagnostics) : IMediaSourceProviderFactory
{
    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.Desktop;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        var settings = (DesktopCaptureSourceSettings)MediaSourceSettingsSerializer.Deserialize(
            sourceDefinition.TypeId,
            sourceDefinition.Settings);

        if (settings.AdapterIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDefinition), "Desktop adapter index must be non-negative.");

        if (settings.OutputIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDefinition), "Desktop output index must be non-negative.");

        var adapterIndex = (uint)settings.AdapterIndex;
        var outputIndex = (uint)settings.OutputIndex;
        var source = DesktopMonitorEnumerator
            .Enumerate()
            .FirstOrDefault(display =>
                display.AdapterIndex == adapterIndex &&
                display.OutputIndex == outputIndex);

        if (source is null)
        {
            throw new InvalidOperationException(
                $"Desktop display adapter={settings.AdapterIndex}, output={settings.OutputIndex} was not found.");
        }

        return new DesktopDuplicationFrameProvider(sourceDefinition.Id, source, diagnostics);
    }
}
