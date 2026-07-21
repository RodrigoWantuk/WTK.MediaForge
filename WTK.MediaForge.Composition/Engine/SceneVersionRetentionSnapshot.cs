namespace WTK.MediaForge.Composition.Engine;

public sealed record SceneVersionRetentionSnapshot
{
    public int RetainedVersionCount { get; init; }

    public int PinnedVersionCount { get; init; }

    public int DirectPinnedVersionCount { get; init; }

    public int TransitivePinnedVersionCount { get; init; }

    public long DiscardedVersionCount { get; init; }

    public int HighWaterMark { get; init; }

    public int MaximumRecentVersionsPerCanvas { get; init; }

    public long ResolutionFailureCount { get; init; }
}
