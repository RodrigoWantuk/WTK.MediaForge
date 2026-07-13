using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime;

internal interface IEncodedOutputRouteFactory
{
    bool CanCreate(RenderOutputTypeId typeId);

    ValueTask RegisterAsync(
        MediaForgeProject project,
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        CancellationToken cancellationToken);
}
