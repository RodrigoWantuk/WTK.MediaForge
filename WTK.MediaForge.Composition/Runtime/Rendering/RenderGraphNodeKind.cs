using WTK.MediaForge.Composition.Runtime.Scene;
using WTK.MediaForge.Composition.Runtime.Scheduling;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum RenderGraphNodeKind
{
    Source = 0,
    Transform = 1,
    Blend = 2,
    Output = 3
}
