namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct DrawObjectId(Guid Value)
{
    public static DrawObjectId New() => new(Guid.NewGuid());

    public static DrawObjectId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("DrawObjectId cannot be Guid.Empty.", nameof(value));

        return new DrawObjectId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
