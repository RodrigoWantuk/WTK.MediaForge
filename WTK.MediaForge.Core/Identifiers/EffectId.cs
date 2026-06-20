namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct EffectId(Guid Value)
{
    public static EffectId New() => new(Guid.NewGuid());

    public static EffectId From(Guid value) => new(value);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
