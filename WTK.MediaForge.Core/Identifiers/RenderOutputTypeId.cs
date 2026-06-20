namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct RenderOutputTypeId(string Value)
{
    public static RenderOutputTypeId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("RenderOutputTypeId cannot be empty.", nameof(value));

        return new RenderOutputTypeId(value);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}
