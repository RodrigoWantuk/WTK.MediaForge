using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

internal interface IRenderOutputSinkFactory
{
    bool CanCreate(RenderOutputTypeId typeId);

    IRenderOutputSink CreateSink(RenderOutputTarget target);
}

