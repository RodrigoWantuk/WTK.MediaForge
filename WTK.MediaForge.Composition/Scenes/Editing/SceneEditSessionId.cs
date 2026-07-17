namespace WTK.MediaForge.Composition.Scenes.Editing;

public readonly record struct SceneEditSessionId(Guid Value)
{
    public static SceneEditSessionId New() => new(Guid.NewGuid());

    public static SceneEditSessionId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("SceneEditSessionId cannot be Guid.Empty.", nameof(value));

        return new SceneEditSessionId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
