using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class CompositeMediaSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly IMediaSourceProviderFactory[] _factories;

    public CompositeMediaSourceProviderFactory(params IMediaSourceProviderFactory[] factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        if (factories.Length == 0)
            throw new ArgumentException("At least one source provider factory is required.", nameof(factories));

        _factories = factories.ToArray();
        if (_factories.Any(factory => factory is null))
            throw new ArgumentException("Source provider factories cannot contain null entries.", nameof(factories));
    }

    public bool CanCreate(MediaSourceTypeId typeId)
    {
        foreach (var factory in _factories)
        {
            if (factory.CanCreate(typeId))
                return true;
        }

        return false;
    }

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);

        foreach (var factory in _factories)
        {
            if (factory.CanCreate(sourceDefinition.TypeId))
                return factory.CreateProvider(sourceDefinition);
        }

        throw new MediaForgeUnsupportedFeatureException(
            sourceDefinition.TypeId.Value,
            $"No media source provider is registered for source type '{sourceDefinition.TypeId.Value}'.");
    }
}
