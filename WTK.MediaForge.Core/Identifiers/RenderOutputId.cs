namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct RenderOutputId(Guid Value)
{
    public static RenderOutputId New() => new(Guid.NewGuid());

    public static RenderOutputId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("RenderOutputId cannot be Guid.Empty.", nameof(value));

        return new RenderOutputId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
