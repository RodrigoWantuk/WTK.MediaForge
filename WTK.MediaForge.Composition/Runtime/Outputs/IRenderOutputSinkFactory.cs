using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

internal interface IRenderOutputSinkFactory
{
    bool CanCreate(RenderOutputTypeId typeId);

    IRenderOutputSink CreateSink(RenderOutputTarget target);
}

internal sealed class UnsupportedRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) => false;

    public IRenderOutputSink CreateSink(RenderOutputTarget target) =>
        throw new NotSupportedException($"No output sink factory registered for type '{target.TypeId.Value}'.");
}
