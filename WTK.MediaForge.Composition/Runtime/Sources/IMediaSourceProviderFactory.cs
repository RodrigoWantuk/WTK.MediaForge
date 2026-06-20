using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Runtime.Sources;

public interface IMediaSourceProviderFactory
{
    bool CanCreate(MediaSourceTypeId typeId);

    IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition);
}

public sealed class UnsupportedMediaSourceProviderFactory : IMediaSourceProviderFactory
{
    public bool CanCreate(MediaSourceTypeId typeId) => false;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition) =>
        throw new NotSupportedException($"No provider factory registered for source type '{sourceDefinition.TypeId.Value}'.");
}
