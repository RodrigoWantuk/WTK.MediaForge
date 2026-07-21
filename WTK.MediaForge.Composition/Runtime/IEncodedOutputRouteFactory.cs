using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime;

internal interface IEncodedOutputRouteFactory
{
    bool CanCreate(RenderOutputTypeId typeId);

    RenderOutputId ResolveSurfaceOutputId(
        MediaForgeProject project,
        MediaForgeRenderOutput output) => output.Id;

    ValueTask RegisterAsync(
        MediaForgeProject project,
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        CancellationToken cancellationToken);

    async ValueTask UnregisterAsync(
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _ = await runtime.RemoveEncodedOutputSinkAsync(output.Id, timeout, cancellationToken).ConfigureAwait(false);

    async ValueTask RecreateAsync(
        MediaForgeProject project,
        MediaForgeRenderOutput output,
        MediaPipelineRuntime runtime,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await runtime.UnregisterEncodedOutputAsync(output.Id, timeout, cancellationToken).ConfigureAwait(false);
        await RegisterAsync(project, output, runtime, cancellationToken).ConfigureAwait(false);
    }
}
