using System.Collections.Immutable;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Composition.Effects;

[Flags]
public enum EffectScope
{
    None = 0,
    Source = 1,
    Layer = 2,
    Canvas = 4
}

public enum EffectAlphaBehavior
{
    Preserves = 0,
    Modifies = 1,
    RequiresStraight = 2,
    RequiresPremultiplied = 3
}

public enum EffectColorSpaceRequirement
{
    Any = 0,
    Linear = 1,
    Srgb = 2,
    Rec709 = 3
}

public enum EffectPassClass
{
    InlineFragment = 0,
    Spatial = 1,
    Temporal = 2,
    Lookup = 3
}

[Flags]
public enum EffectHardwareRequirement
{
    None = 0,
    Vulkan = 1,
    ComputeShader = 2,
    Texture3D = 4,
    TemporalHistory = 8
}

public sealed record EffectCapabilityDescriptor(
    Type EffectType,
    EffectScope AcceptedScopes,
    ImmutableArray<RenderPixelFormat> AcceptedFormats,
    EffectAlphaBehavior AlphaBehavior,
    EffectColorSpaceRequirement ColorSpaceRequirement,
    EffectPassClass PassClass,
    bool IsTemporal,
    bool SupportsMask,
    EffectHardwareRequirement HardwareRequirements)
{
    public bool AcceptsScope(EffectScope scope) =>
        scope is not EffectScope.None && (AcceptedScopes & scope) == scope;

    public bool AcceptsFormat(RenderPixelFormat format) =>
        AcceptedFormats.IsDefaultOrEmpty || AcceptedFormats.Contains(format);

    public bool AcceptsColorSpace(RenderColorSpace colorSpace) =>
        ColorSpaceRequirement switch
        {
            EffectColorSpaceRequirement.Any => true,
            EffectColorSpaceRequirement.Srgb => colorSpace == RenderColorSpace.Srgb,
            EffectColorSpaceRequirement.Rec709 => colorSpace is RenderColorSpace.Rec709Full or RenderColorSpace.Rec709Limited,
            EffectColorSpaceRequirement.Linear => false,
            _ => false
        };
}

public sealed class EffectCapabilityRegistry
{
    private static readonly ImmutableArray<RenderPixelFormat> ColorFormats =
        [RenderPixelFormat.Rgba8Unorm, RenderPixelFormat.Bgra8Unorm];

    private readonly ImmutableDictionary<Type, EffectCapabilityDescriptor> _descriptors;

    public static EffectCapabilityRegistry Default { get; } = new(
    [
        new(
            typeof(ChromaKeyEffect),
            EffectScope.Source | EffectScope.Layer,
            ColorFormats,
            EffectAlphaBehavior.Modifies,
            EffectColorSpaceRequirement.Any,
            EffectPassClass.InlineFragment,
            IsTemporal: false,
            SupportsMask: true,
            EffectHardwareRequirement.Vulkan),
        new(
            typeof(ColorCorrectionEffect),
            EffectScope.Source | EffectScope.Layer | EffectScope.Canvas,
            ColorFormats,
            EffectAlphaBehavior.Preserves,
            EffectColorSpaceRequirement.Any,
            EffectPassClass.InlineFragment,
            IsTemporal: false,
            SupportsMask: true,
            EffectHardwareRequirement.Vulkan),
        new(
            typeof(BlurEffect),
            EffectScope.Layer | EffectScope.Canvas,
            ColorFormats,
            EffectAlphaBehavior.Preserves,
            EffectColorSpaceRequirement.Any,
            EffectPassClass.Spatial,
            IsTemporal: false,
            SupportsMask: true,
            EffectHardwareRequirement.Vulkan)
    ]);

    public EffectCapabilityRegistry(IEnumerable<EffectCapabilityDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = descriptors.ToImmutableDictionary(static item => item.EffectType);
    }

    public IReadOnlyCollection<EffectCapabilityDescriptor> Descriptors => _descriptors.Values.ToArray();

    public bool TryGet(Type effectType, out EffectCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(effectType);
        return _descriptors.TryGetValue(effectType, out descriptor!);
    }

    public EffectCapabilityDescriptor GetRequired(MediaForgeEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return TryGet(effect.GetType(), out var descriptor)
            ? descriptor
            : throw new NotSupportedException($"Effect type '{effect.GetType().FullName}' has no capability descriptor.");
    }
}
