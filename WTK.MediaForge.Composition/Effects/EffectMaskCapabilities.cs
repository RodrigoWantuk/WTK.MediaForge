using System.Collections.Immutable;

namespace WTK.MediaForge.Composition.Effects;

/// <summary>
/// Separates persisted model support from actual runtime, GPU and Studio support.
/// A mask definition is never made editable merely because it can be serialized.
/// </summary>
public sealed record EffectMaskCapabilityDescriptor(
    Type MaskType,
    bool ModelSupported,
    bool RuntimeSupported,
    bool GpuBackendSupported,
    bool StudioEditable,
    bool ProductAvailable,
    bool TransformSupported,
    string? UnavailableReason)
{
    public bool IsExecutable => RuntimeSupported && GpuBackendSupported && ProductAvailable;
}

public sealed class EffectMaskCapabilityRegistry
{
    private readonly ImmutableDictionary<Type, EffectMaskCapabilityDescriptor> _descriptors;

    public static EffectMaskCapabilityRegistry Default { get; } = new(
    [
        Available(typeof(RectangleEffectMask)),
        Available(typeof(RoundedRectangleEffectMask)),
        Available(typeof(EllipseEffectMask)),
        Unavailable(typeof(ImageAlphaEffectMask), "Image-alpha masks require GPU asset sampling and edge/ROI handling."),
        Unavailable(typeof(LumaEffectMask), "Luma masks require GPU asset sampling and luma evaluation."),
        Unavailable(typeof(GradientEffectMask), "Gradient masks require the complete Vulkan gradient and ROI contract.")
    ]);

    public EffectMaskCapabilityRegistry(IEnumerable<EffectMaskCapabilityDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = descriptors.ToImmutableDictionary(static descriptor => descriptor.MaskType);
    }

    public IReadOnlyCollection<EffectMaskCapabilityDescriptor> Descriptors => _descriptors.Values.ToArray();

    public bool TryGet(Type maskType, out EffectMaskCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(maskType);
        return _descriptors.TryGetValue(maskType, out descriptor!);
    }

    public EffectMaskCapabilityDescriptor GetRequired(EffectMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return TryGet(mask.GetType(), out var descriptor)
            ? descriptor
            : throw new NotSupportedException($"Mask type '{mask.GetType().FullName}' has no capability descriptor.");
    }

    private static EffectMaskCapabilityDescriptor Available(Type type) =>
        new(type, ModelSupported: true, RuntimeSupported: true, GpuBackendSupported: true,
            StudioEditable: false, ProductAvailable: true, TransformSupported: false,
            UnavailableReason: "Geometric mask transform editing is unavailable until the complete transform/ROI contract is executed.");

    private static EffectMaskCapabilityDescriptor Unavailable(Type type, string reason) =>
        new(type, ModelSupported: true, RuntimeSupported: false, GpuBackendSupported: false,
            StudioEditable: false, ProductAvailable: false, TransformSupported: false,
            UnavailableReason: reason);
}
