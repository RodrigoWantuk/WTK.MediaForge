namespace WTK.MediaForge.Composition.Scenes.Editing;

public readonly record struct SceneVersionBinding(
    SceneVersionBindingKind Kind,
    SceneEditSessionId? DraftSessionId,
    SceneVersionId? ExplicitVersionId)
{
    public static SceneVersionBinding Published { get; } =
        new(SceneVersionBindingKind.Published, null, null);

    public static SceneVersionBinding DraftForSession(SceneEditSessionId sessionId)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("Draft scene binding requires a non-empty edit session id.", nameof(sessionId));

        return new SceneVersionBinding(SceneVersionBindingKind.Draft, sessionId, null);
    }

    public static SceneVersionBinding ExplicitVersion(SceneVersionId versionId)
    {
        if (versionId.IsEmpty)
            throw new ArgumentException("Explicit scene binding requires a non-empty version id.", nameof(versionId));

        return new SceneVersionBinding(SceneVersionBindingKind.ExplicitVersion, null, versionId);
    }

    public void Validate()
    {
        switch (Kind)
        {
            case SceneVersionBindingKind.Published:
                if (DraftSessionId is not null || ExplicitVersionId is not null)
                    throw new InvalidOperationException("Published scene binding must not carry draft or explicit version ids.");
                break;

            case SceneVersionBindingKind.Draft:
                if (DraftSessionId is not { IsEmpty: false } || ExplicitVersionId is not null)
                    throw new InvalidOperationException("Draft scene binding must carry exactly one draft session id.");
                break;

            case SceneVersionBindingKind.ExplicitVersion:
                if (ExplicitVersionId is not { IsEmpty: false } || DraftSessionId is not null)
                    throw new InvalidOperationException("Explicit scene binding must carry exactly one scene version id.");
                break;

            default:
                throw new InvalidOperationException($"Unsupported scene version binding kind '{Kind}'.");
        }
    }
}
