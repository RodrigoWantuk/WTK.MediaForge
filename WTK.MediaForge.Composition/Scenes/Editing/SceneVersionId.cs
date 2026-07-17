namespace WTK.MediaForge.Composition.Scenes.Editing;

public readonly record struct SceneVersionId(Guid Value)
{
    public static SceneVersionId New() => new(Guid.NewGuid());

    public static SceneVersionId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("SceneVersionId cannot be Guid.Empty.", nameof(value));

        return new SceneVersionId(value);
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
