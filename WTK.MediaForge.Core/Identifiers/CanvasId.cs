namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct CanvasId(Guid Value)
{
    public static CanvasId New() => new(Guid.NewGuid());

    public static CanvasId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CanvasId cannot be Guid.Empty.", nameof(value));

        return new CanvasId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
