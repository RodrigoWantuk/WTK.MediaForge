namespace WTK.MediaForge.Composition.Outputs;

public readonly record struct RenderOutputSinkId(Guid Value)
{
    public static RenderOutputSinkId New() => new(Guid.NewGuid());

    public static RenderOutputSinkId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("RenderOutputSinkId cannot be Guid.Empty.", nameof(value));

        return new RenderOutputSinkId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
