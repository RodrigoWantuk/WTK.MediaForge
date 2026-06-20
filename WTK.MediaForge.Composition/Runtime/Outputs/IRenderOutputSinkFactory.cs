using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

public interface IRenderOutputSinkFactory
{
    bool CanCreate(RenderOutputTypeId typeId);

    object CreateSink(RenderOutputTarget target);
}

public sealed class UnsupportedRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) => false;

    public object CreateSink(RenderOutputTarget target) =>
        throw new NotSupportedException($"No output sink factory registered for type '{target.TypeId.Value}'.");
}
