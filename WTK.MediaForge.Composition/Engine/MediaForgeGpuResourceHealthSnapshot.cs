namespace WTK.MediaForge.Composition.Engine;

public sealed record MediaForgeGpuResourceHealthSnapshot
{
    public int PendingSubmissions { get; init; }

    public int ExternalTextureImports { get; init; }

    public int BoundOutputTargets { get; init; }

    public int CachedIntermediateTargets { get; init; }

    public int ActiveIntermediateBorrows { get; init; }

    public int RetiredIntermediateTargets { get; init; }

    public int ActivePooledTextures { get; init; }

    public int AvailablePooledTextures { get; init; }

    public int PendingFenceTextures { get; init; }

    public int PendingRetiredResources { get; init; }

    public int FailedRetiredResources { get; init; }

    public int LiveFramebuffers { get; init; }

    public int LiveDescriptorSets { get; init; }

    public int FramebufferHighWaterMark { get; init; }

    public int DescriptorSetHighWaterMark { get; init; }

    public int PooledTextureHighWaterMark { get; init; }

    public int IntermediateTargetHighWaterMark { get; init; }
}
