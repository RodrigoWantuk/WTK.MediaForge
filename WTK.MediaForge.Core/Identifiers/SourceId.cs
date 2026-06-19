namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct SourceId(Guid Value)
{
    public static SourceId New() => new(Guid.NewGuid());

    public static SourceId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("SourceId cannot be Guid.Empty.", nameof(value));

        return new SourceId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
