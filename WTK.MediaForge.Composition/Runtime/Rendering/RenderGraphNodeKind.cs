using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum RenderGraphNodeKind
{
    Source = 0,
    Transform = 1,
    Primitive = 2,
    Blend = 3,
    Transition = 4,
    Output = 5
}
