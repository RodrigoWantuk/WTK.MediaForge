using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime;

internal sealed class CompositionRuntime
{
    private readonly SourceRuntimeManager _sourceRuntimeManager;

    public CompositionRuntime(IMediaForgeDiagnosticsSink? diagnostics = null)
        : this(new SourceRuntimeManager(diagnostics))
    {
    }

    public CompositionRuntime(SourceRuntimeManager sourceRuntimeManager) =>
        _sourceRuntimeManager = sourceRuntimeManager ?? throw new ArgumentNullException(nameof(sourceRuntimeManager));

    public void RegisterFrameProvider(IVideoFrameProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _sourceRuntimeManager.RegisterProvider(provider);
    }

    public void UnregisterFrameProvider(SourceId sourceId) =>
        _sourceRuntimeManager.UnregisterProvider(sourceId);

    public bool TryGetFrameProvider(SourceId sourceId, out IVideoFrameProvider provider) =>
        _sourceRuntimeManager.TryGetProvider(sourceId, out provider!);

    public SourceFrameAcquireResult TryAcquireFrame(SourceId sourceId, TimeSpan renderTimestamp) =>
        _sourceRuntimeManager.TryAcquireFrame(sourceId, renderTimestamp);
}
