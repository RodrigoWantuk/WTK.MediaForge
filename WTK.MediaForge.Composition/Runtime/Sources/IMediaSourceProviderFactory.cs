using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal interface IMediaSourceProviderFactory
{
    bool CanCreate(MediaSourceTypeId typeId);

    IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition);
}

