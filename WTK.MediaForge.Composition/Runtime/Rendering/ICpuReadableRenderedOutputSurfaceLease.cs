using WTK.MediaForge.Composition.Outputs;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface ICpuReadableRenderedOutputSurfaceLease
{
    ValueTask<CpuReadbackFrame> ReadCpuFrameAsync(
        RenderOutputFrameInfo info,
        CancellationToken cancellationToken);
}
