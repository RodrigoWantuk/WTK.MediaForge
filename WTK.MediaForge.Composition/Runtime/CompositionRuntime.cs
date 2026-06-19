using System.Collections.Concurrent;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Runtime;

public sealed class CompositionRuntime
{
    private readonly ConcurrentDictionary<SourceId, IVideoFrameProvider> _frameProviders = new();

    public void RegisterFrameProvider(IVideoFrameProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Id.IsEmpty)
            throw new ArgumentException("Provider SourceId cannot be empty.", nameof(provider));

        _frameProviders[provider.Id] = provider;
    }

    public void UnregisterFrameProvider(SourceId sourceId) =>
        _frameProviders.TryRemove(sourceId, out _);

    public bool TryGetFrameProvider(SourceId sourceId, out IVideoFrameProvider provider) =>
        _frameProviders.TryGetValue(sourceId, out provider!);
}
