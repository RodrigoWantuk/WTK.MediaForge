using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Diagnostics;
using System.Collections.Concurrent;

namespace WTK.MediaForge.Composition.Runtime.Recovery;

internal enum FaultRecoveryScenario
{
    VulkanDeviceLost,
    EncoderUnavailable,
    DecoderUnavailable,
    MonitorDisconnected,
    WebcamRemoved,
    GpuSwitch,
    RenderExportFailed,
    RtmpDisconnected,
    Mp4FinalizeFailed,
    SourceProviderFailed
}

internal enum FaultRecoveryStatus
{
    Recovering,
    Recovered,
    Exhausted,
    Canceled
}

internal sealed record FaultRecoveryPolicy
{
    public int MaxAttempts { get; init; } = 5;

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public bool RequiresRecordingPause { get; init; }

    public bool RequiresStreamingPause { get; init; }

    public bool RequiresSourceIsolation { get; init; }

    public static FaultRecoveryPolicy ForScenario(FaultRecoveryScenario scenario) =>
        scenario switch
        {
            FaultRecoveryScenario.VulkanDeviceLost => new FaultRecoveryPolicy
            {
                RequiresRecordingPause = true,
                RequiresStreamingPause = true,
                InitialBackoff = TimeSpan.FromMilliseconds(100),
                MaxBackoff = TimeSpan.FromSeconds(2)
            },
            FaultRecoveryScenario.EncoderUnavailable => new FaultRecoveryPolicy
            {
                RequiresRecordingPause = true,
                RequiresStreamingPause = true
            },
            FaultRecoveryScenario.GpuSwitch => new FaultRecoveryPolicy
            {
                RequiresRecordingPause = true,
                RequiresStreamingPause = true
            },
            FaultRecoveryScenario.RenderExportFailed => new FaultRecoveryPolicy
            {
                RequiresRecordingPause = true,
                RequiresStreamingPause = true
            },
            FaultRecoveryScenario.RtmpDisconnected => new FaultRecoveryPolicy
            {
                RequiresStreamingPause = true,
                InitialBackoff = TimeSpan.FromMilliseconds(500),
                MaxBackoff = TimeSpan.FromSeconds(10)
            },
            FaultRecoveryScenario.Mp4FinalizeFailed => new FaultRecoveryPolicy
            {
                RequiresRecordingPause = true,
                MaxAttempts = 1
            },
            FaultRecoveryScenario.WebcamRemoved => new FaultRecoveryPolicy
            {
                RequiresSourceIsolation = true,
                InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(10)
            },
            FaultRecoveryScenario.SourceProviderFailed => new FaultRecoveryPolicy
            {
                RequiresSourceIsolation = true
            },
            _ => new FaultRecoveryPolicy()
        };
}

internal sealed record FaultRecoveryState
{
    public required string ResourceKey { get; init; }

    public FaultRecoveryScenario Scenario { get; init; }

    public FaultRecoveryStatus Status { get; init; } = FaultRecoveryStatus.Recovering;

    public string Detail { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTimeOffset LastAttemptUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool RequiresRecordingPause { get; init; }

    public bool RequiresStreamingPause { get; init; }

    public bool RequiresSourceIsolation { get; init; }
}

/// <summary>
/// Coordinates automatic recovery for GPU/media faults without requiring application restart.
/// </summary>
internal sealed class FaultRecoveryCoordinator
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Func<FaultRecoveryScenario, FaultRecoveryPolicy> _policyProvider;
    private readonly Dictionary<string, FaultRecoveryState> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _recoveryGates =
        new(StringComparer.Ordinal);

    public FaultRecoveryCoordinator(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        Func<FaultRecoveryScenario, FaultRecoveryPolicy>? policyProvider = null)
    {
        _diagnostics = diagnostics;
        _policyProvider = policyProvider ?? FaultRecoveryPolicy.ForScenario;
    }

    public event EventHandler<FaultRecoveryState>? RecoveryStateChanged;

    public IReadOnlyDictionary<string, FaultRecoveryState> States
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, FaultRecoveryState>(_states, StringComparer.Ordinal);
        }
    }

    public bool IsRecordingPaused
    {
        get
        {
            lock (_gate)
                return _states.Values.Any(state => state.RequiresRecordingPause);
        }
    }

    public bool IsStreamingPaused
    {
        get
        {
            lock (_gate)
                return _states.Values.Any(state => state.RequiresStreamingPause);
        }
    }

    public bool HasIsolatedSources
    {
        get
        {
            lock (_gate)
                return _states.Values.Any(state => state.RequiresSourceIsolation);
        }
    }

    public async Task<FaultRecoveryState> HandleFaultAsync(
        FaultRecoveryScenario scenario,
        string detail,
        Func<CancellationToken, Task<bool>> recoveryAction,
        CancellationToken cancellationToken = default) =>
        await HandleFaultAsync(
                scenario,
                scenario.ToString(),
                detail,
                recoveryAction,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<FaultRecoveryState> HandleFaultAsync(
        FaultRecoveryScenario scenario,
        string resourceKey,
        string detail,
        Func<CancellationToken, Task<bool>> recoveryAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentNullException.ThrowIfNull(recoveryAction);

        var recoveryGate = _recoveryGates.GetOrAdd(resourceKey, static _ => new SemaphoreSlim(1, 1));
        await recoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await HandleFaultCoreAsync(scenario, resourceKey, detail, recoveryAction, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            recoveryGate.Release();
        }
    }

    private async Task<FaultRecoveryState> HandleFaultCoreAsync(
        FaultRecoveryScenario scenario,
        string resourceKey,
        string detail,
        Func<CancellationToken, Task<bool>> recoveryAction,
        CancellationToken cancellationToken)
    {

        var policy = ValidatePolicy(_policyProvider(scenario));
        var attempt = 0;
        var backoff = policy.InitialBackoff;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                var canceledState = CreateState(
                    scenario,
                    resourceKey,
                    $"{detail} (recovery canceled)",
                    attempt,
                    FaultRecoveryStatus.Canceled,
                    policy);
                PublishState(canceledState);
                cancellationToken.ThrowIfCancellationRequested();
            }

            attempt++;
            var state = CreateState(
                scenario,
                resourceKey,
                detail,
                attempt,
                FaultRecoveryStatus.Recovering,
                policy);
            PublishState(state);

            try
            {
                if (await recoveryAction(cancellationToken).ConfigureAwait(false))
                {
                    var recoveredState = CreateState(
                        scenario,
                        resourceKey,
                        $"{detail} (recovered)",
                        state.AttemptCount,
                        FaultRecoveryStatus.Recovered,
                        policy);
                    PublishState(recoveredState);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Info,
                        "engine.fault_recovery_succeeded",
                        $"Recovered from {scenario}.",
                        nameof(FaultRecoveryCoordinator));
                    ClearState(resourceKey);
                    return recoveredState;
                }
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Warning,
                    "engine.fault_recovery_attempt_failed",
                    $"Fault recovery attempt failed for {scenario}.",
                    nameof(FaultRecoveryCoordinator),
                    ex);
            }

            if (attempt >= policy.MaxAttempts)
                break;

            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, policy.MaxBackoff.TotalMilliseconds));
        }

        var failedState = CreateState(
            scenario,
            resourceKey,
            $"{detail} (recovery exhausted)",
            attempt,
            FaultRecoveryStatus.Exhausted,
            policy);
        PublishState(failedState);
        return failedState;
    }

    public void NotifyEncoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.EncoderUnavailable, "encoder", detail, 1));

    public void NotifyDecoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.DecoderUnavailable, "decoder", detail, 1));

    public void NotifyRtmpDisconnected(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.RtmpDisconnected, "streaming:rtmp", detail, 1));

    public void NotifyRenderExportFailed(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.RenderExportFailed, "render-export", detail, 1));

    public void NotifySourceProviderFailed(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.SourceProviderFailed, "source", detail, 1));

    public void NotifyVulkanDeviceLost(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.VulkanDeviceLost, "graphics-device", detail, 1));

    public void ClearState(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        lock (_gate)
            _states.Remove(resourceKey);
    }

    public void ClearState(FaultRecoveryScenario scenario)
    {
        lock (_gate)
        {
            foreach (var key in _states
                         .Where(pair => pair.Value.Scenario == scenario)
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _states.Remove(key);
            }
        }
    }

    private FaultRecoveryState CreateState(
        FaultRecoveryScenario scenario,
        string resourceKey,
        string detail,
        int attempt) =>
        CreateState(
            scenario,
            resourceKey,
            detail,
            attempt,
            FaultRecoveryStatus.Recovering,
            ValidatePolicy(_policyProvider(scenario)));

    private static FaultRecoveryState CreateState(
        FaultRecoveryScenario scenario,
        string resourceKey,
        string detail,
        int attempt,
        FaultRecoveryStatus status,
        FaultRecoveryPolicy policy) =>
        new()
        {
            ResourceKey = resourceKey,
            Scenario = scenario,
            Status = status,
            Detail = detail,
            AttemptCount = attempt,
            RequiresRecordingPause = policy.RequiresRecordingPause,
            RequiresStreamingPause = policy.RequiresStreamingPause,
            RequiresSourceIsolation = policy.RequiresSourceIsolation
        };

    private static FaultRecoveryPolicy ValidatePolicy(FaultRecoveryPolicy policy)
    {
        if (policy.MaxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "Fault recovery MaxAttempts must be positive.");

        if (policy.InitialBackoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy), "Fault recovery InitialBackoff cannot be negative.");

        if (policy.MaxBackoff < policy.InitialBackoff)
            throw new ArgumentOutOfRangeException(nameof(policy), "Fault recovery MaxBackoff must be greater than or equal to InitialBackoff.");

        return policy;
    }

    private void PublishState(FaultRecoveryState state)
    {
        lock (_gate)
            _states[state.ResourceKey] = state;

        RecoveryStateChanged?.Invoke(this, state);
    }
}

internal static class MediaForgeEngineFaultRecoveryExtensions
{
    public static FaultRecoveryCoordinator EnsureFaultRecovery(this MediaForgeEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.FaultRecoveryCoordinatorForTests
            ?? throw new InvalidOperationException("Fault recovery coordinator is not attached to the engine.");
    }
}
