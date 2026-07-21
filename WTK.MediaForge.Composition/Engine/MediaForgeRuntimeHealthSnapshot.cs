using WTK.MediaForge.Composition.Outputs;
namespace WTK.MediaForge.Composition.Engine;

public enum MediaForgeRuntimeHealthStatus
{
    Stopped,
    Healthy,
    Degraded,
    Recovering,
    Failed
}

public enum MediaForgeRecoveryArea
{
    GraphicsDevice,
    Source,
    Decoder,
    Encoder,
    Recording,
    Streaming,
    Output
}

public enum MediaForgeRecoveryStatus
{
    Recovering,
    Recovered,
    Exhausted,
    Canceled
}

public sealed record MediaForgeRecoverySnapshot
{
    public required string ResourceId { get; init; }

    public required MediaForgeRecoveryArea Area { get; init; }

    public required MediaForgeRecoveryStatus Status { get; init; }

    public required string Message { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset LastAttemptUtc { get; init; }

    public bool PausesRecording { get; init; }

    public bool PausesStreaming { get; init; }

    public bool IsolatesSource { get; init; }
}

public sealed class MediaForgeRecoveryEventArgs(MediaForgeRecoverySnapshot recovery) : EventArgs
{
    public MediaForgeRecoverySnapshot Recovery { get; } =
        recovery ?? throw new ArgumentNullException(nameof(recovery));
}

public sealed record MediaForgeRuntimeHealthSnapshot
{
    public required DateTimeOffset CapturedAt { get; init; }

    public required MediaForgeRuntimeHealthStatus Status { get; init; }

    public required MediaForgeEngineState EngineState { get; init; }

    public IReadOnlyList<EncodedOutputRuntimeSnapshot> EncodedOutputs { get; init; } =
        Array.Empty<EncodedOutputRuntimeSnapshot>();

    public IReadOnlyList<MediaForgeRecoverySnapshot> Recoveries { get; init; } =
        Array.Empty<MediaForgeRecoverySnapshot>();

    public SceneVersionRetentionSnapshot SceneVersions { get; init; } = new();

    public MediaForgeGpuResourceHealthSnapshot GpuResources { get; init; } = new();
}
