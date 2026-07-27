using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media;

namespace WTK.MediaForge.Windows;

public sealed class WindowsMediaForgeRuntimeFactory : IMediaForgeRuntimeFactory
{
    public ValueTask<MediaForgeRuntime> CreateAsync(RuntimeCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new WindowsHardwareMediaCapabilityProbe();
        return ValueTask.FromResult(MediaForgeRuntime.Available(
            MediaForgeWindows.CreateEngine(),
            probe,
            MediaForgeRuntimeAdapterCatalog.Known,
            MediaForgeWindows.GetCapabilitySnapshotAsync));
    }
}
