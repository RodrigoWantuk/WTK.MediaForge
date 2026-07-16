using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Recovery;

public enum FaultRecoveryScenario
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

public enum FaultRecoveryStatus
{
    Recovering,
    Recovered,
    Exhausted,
    Canceled
}

public sealed record FaultRecoveryPolicy
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

public sealed record FaultRecoveryState
{
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
public sealed class FaultRecoveryCoordinator
{
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Func<FaultRecoveryScenario, FaultRecoveryPolicy> _policyProvider;
    private readonly Dictionary<FaultRecoveryScenario, FaultRecoveryState> _states = new();
    private readonly object _gate = new();

    public FaultRecoveryCoordinator(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        Func<FaultRecoveryScenario, FaultRecoveryPolicy>? policyProvider = null)
    {
        _diagnostics = diagnostics;
        _policyProvider = policyProvider ?? FaultRecoveryPolicy.ForScenario;
    }

    public event EventHandler<FaultRecoveryState>? RecoveryStateChanged;

    public IReadOnlyDictionary<FaultRecoveryScenario, FaultRecoveryState> States
    {
        get
        {
            lock (_gate)
                return new Dictionary<FaultRecoveryScenario, FaultRecoveryState>(_states);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryAction);

        var policy = ValidatePolicy(_policyProvider(scenario));
        var attempt = 0;
        var backoff = policy.InitialBackoff;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                var canceledState = CreateState(
                    scenario,
                    $"{detail} (recovery canceled)",
                    attempt,
                    FaultRecoveryStatus.Canceled,
                    policy);
                PublishState(canceledState);
                cancellationToken.ThrowIfCancellationRequested();
            }

            attempt++;
            var state = CreateState(scenario, detail, attempt, FaultRecoveryStatus.Recovering, policy);
            PublishState(state);

            try
            {
                if (await recoveryAction(cancellationToken).ConfigureAwait(false))
                {
                    var recoveredState = CreateState(
                        scenario,
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
                    ClearState(scenario);
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
            $"{detail} (recovery exhausted)",
            attempt,
            FaultRecoveryStatus.Exhausted,
            policy);
        PublishState(failedState);
        return failedState;
    }

    public void NotifyEncoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.EncoderUnavailable, detail, 1));

    public void NotifyDecoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.DecoderUnavailable, detail, 1));

    public void NotifyRtmpDisconnected(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.RtmpDisconnected, detail, 1));

    public void NotifyRenderExportFailed(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.RenderExportFailed, detail, 1));

    public void NotifySourceProviderFailed(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.SourceProviderFailed, detail, 1));

    public void ClearState(FaultRecoveryScenario scenario)
    {
        lock (_gate)
            _states.Remove(scenario);
    }

    private FaultRecoveryState CreateState(FaultRecoveryScenario scenario, string detail, int attempt) =>
        CreateState(
            scenario,
            detail,
            attempt,
            FaultRecoveryStatus.Recovering,
            ValidatePolicy(_policyProvider(scenario)));

    private static FaultRecoveryState CreateState(
        FaultRecoveryScenario scenario,
        string detail,
        int attempt,
        FaultRecoveryStatus status,
        FaultRecoveryPolicy policy) =>
        new()
        {
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
            _states[state.Scenario] = state;

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
