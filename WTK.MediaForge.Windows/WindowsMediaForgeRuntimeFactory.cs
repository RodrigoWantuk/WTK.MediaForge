using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media;

namespace WTK.MediaForge.Windows;

public sealed class WindowsMediaForgeRuntimeFactory : IMediaForgeRuntimeFactory
{
    /// <summary>Gets the engine created for the current Windows Studio runtime.</summary>
    public MediaForgeEngine? Engine { get; private set; }

    /// <summary>Raised after the Windows runtime creates its engine.</summary>
    public event EventHandler<MediaForgeEngine>? EngineCreated;

    public ValueTask<MediaForgeRuntime> CreateAsync(RuntimeCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new WindowsHardwareMediaCapabilityProbe();
        var engine = MediaForgeWindows.CreateEngine();
        Engine = engine;
        EngineCreated?.Invoke(this, engine);
        return ValueTask.FromResult(MediaForgeRuntime.Available(
            engine,
            probe,
            MediaForgeRuntimeAdapterCatalog.Known,
            MediaForgeWindows.GetCapabilitySnapshotAsync));
    }
}
