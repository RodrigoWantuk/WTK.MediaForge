using System.Numerics;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal struct MediaForgeTextPushConstants
{
    public Vector4 TextColor;

    public float Opacity;
}

internal static partial class CompositionPushConstantsBuilder
{
    public static MediaForgeTextPushConstants BuildText(RenderTextDrawObjectSnapshot text) =>
        new()
        {
            TextColor = ToVector4(text.TextColor),
            Opacity = text.Opacity
        };
}
