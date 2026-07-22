using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal readonly record struct EffectStackFingerprint(string Value)
{
    public static EffectStackFingerprint Create(IEnumerable<EffectStateSnapshot> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var canonical = string.Join(
            '\u001F',
            effects.Select(EffectStateFingerprint.CreateSemanticConfiguration));
        return new EffectStackFingerprint(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }
}

internal sealed record EffectPassPlan(
    int Index,
    EffectScope Scope,
    EffectPassClass PassClass,
    ImmutableArray<EffectStateSnapshot> Effects,
    bool RequiresIntermediateTarget);

internal sealed record EffectExecutionPlan(
    EffectScope Scope,
    ImmutableArray<EffectStateSnapshot> OrderedEffects,
    ImmutableArray<EffectPassPlan> Passes,
    EffectStackFingerprint Fingerprint)
{
    public bool IsEmpty => OrderedEffects.IsDefaultOrEmpty;
}

internal sealed class EffectExecutionPlanner
{
    public static EffectExecutionPlanner Default { get; } = new(EffectCapabilityRegistry.Default);

    private readonly EffectCapabilityRegistry _capabilities;

    public EffectExecutionPlanner(EffectCapabilityRegistry capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public EffectExecutionPlan CreatePlan(
        EffectScope scope,
        IEnumerable<EffectStateSnapshot> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (scope is EffectScope.None || !IsSingleScope(scope))
            throw new ArgumentOutOfRangeException(nameof(scope), "One concrete effect scope is required.");

        var ordered = effects
            .Select((effect, index) => (effect, index))
            .Where(static item => item.effect.Enabled)
            .OrderBy(static item => item.effect.Order)
            .ThenBy(static item => item.index)
            .Select(static item => item.effect)
            .ToImmutableArray();

        var passes = ImmutableArray.CreateBuilder<EffectPassPlan>();
        var group = ImmutableArray.CreateBuilder<EffectStateSnapshot>();
        EffectPassClass? groupClass = null;

        foreach (var effect in ordered)
        {
            var capability = ResolveCapability(effect);
            if (!capability.AcceptsScope(scope))
                throw new InvalidOperationException(
                    $"Effect '{effect.Name}' does not support the {scope} scope.");

            if (groupClass != capability.PassClass && group.Count > 0)
                FlushPass(passes, group, scope, groupClass!.Value);

            groupClass = capability.PassClass;
            group.Add(effect);

            if (capability.PassClass is not EffectPassClass.InlineFragment)
                FlushPass(passes, group, scope, capability.PassClass);
        }

        if (group.Count > 0)
            FlushPass(passes, group, scope, groupClass!.Value);

        return new EffectExecutionPlan(
            scope,
            ordered,
            passes.ToImmutable(),
            EffectStackFingerprint.Create(ordered));
    }

    private EffectCapabilityDescriptor ResolveCapability(EffectStateSnapshot effect)
    {
        var modelType = effect switch
        {
            ChromaKeyEffectSnapshot => typeof(ChromaKeyEffect),
            ColorCorrectionEffectSnapshot => typeof(ColorCorrectionEffect),
            BlurEffectSnapshot => typeof(BlurEffect),
            _ => throw new NotSupportedException(
                $"Effect snapshot '{effect.GetType().FullName}' has no production capability mapping.")
        };

        return _capabilities.TryGet(modelType, out var descriptor)
            ? descriptor
            : throw new NotSupportedException(
                $"Effect type '{modelType.FullName}' has no capability descriptor.");
    }

    private static void FlushPass(
        ImmutableArray<EffectPassPlan>.Builder passes,
        ImmutableArray<EffectStateSnapshot>.Builder effects,
        EffectScope scope,
        EffectPassClass passClass)
    {
        passes.Add(new EffectPassPlan(
            passes.Count,
            scope,
            passClass,
            effects.ToImmutable(),
            passClass is not EffectPassClass.InlineFragment));
        effects.Clear();
    }

    private static bool IsSingleScope(EffectScope scope) =>
        scope is EffectScope.Source or EffectScope.Layer or EffectScope.Canvas;
}
