using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Runtime;

public sealed class CompositionRuntime
{
    private readonly Dictionary<SourceId, IVideoFrameProvider> _frameProviders = new();

    public void RegisterFrameProvider(IVideoFrameProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _frameProviders[provider.Id] = provider;
    }

    public void UnregisterFrameProvider(SourceId sourceId) =>
        _frameProviders.Remove(sourceId);

    public bool TryGetFrameProvider(SourceId sourceId, out IVideoFrameProvider provider) =>
        _frameProviders.TryGetValue(sourceId, out provider!);
}
