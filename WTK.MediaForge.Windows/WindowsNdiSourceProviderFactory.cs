using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Windows.Media.Ndi;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsNdiSourceProviderFactory(
    IMediaForgeDiagnosticsSink? diagnostics = null,
    IWindowsNdiRuntimeProbe? runtimeProbe = null) : IMediaSourceProviderFactory
{
    private readonly IWindowsNdiRuntimeProbe _runtimeProbe = runtimeProbe ?? new WindowsNdiRuntimeProbe();

    public bool CanCreate(MediaSourceTypeId typeId) =>
        MediaSourceTypeRegistry.ResolveCanonical(typeId) == MediaSourceTypes.NdiInput;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        var runtime = _runtimeProbe.Probe();
        var message = runtime.CanUseStandardSdk
            ? $"NDI Standard SDK runtime is installed at '{runtime.LibraryPath}', but NDI video input is not enabled because Standard SDK receive exposes frame buffers and has not proven GPU-safe source leases. Continuous raw CPU NDI frames are prohibited in the product path. Use MediaForgeWindows.FindNdiSourcesAsync for safe source discovery."
            : $"NDI input is unavailable. {runtime.Reason}";

        var exception = new MediaForgeUnsupportedFeatureException(
            $"source.{MediaSourceTypes.NdiInput.Value}",
            message);

        MediaForgeDiagnostics.Report(
            diagnostics,
            MediaForgeDiagnosticSeverity.Error,
            "source.ndi_unavailable",
            message,
            nameof(WindowsNdiSourceProviderFactory),
            exception,
            sourceDefinition.Id.Value,
            sourceDefinition.Name);

        throw exception;
    }
}
