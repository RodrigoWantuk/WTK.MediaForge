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
    GpuSwitch
}

public sealed record FaultRecoveryState
{
    public FaultRecoveryScenario Scenario { get; init; }

    public string Detail { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTimeOffset LastAttemptUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool RequiresRecordingPause { get; init; }

    public bool RequiresStreamingPause { get; init; }
}

/// <summary>
/// Coordinates automatic recovery for GPU/media faults without requiring application restart.
/// </summary>
public sealed class FaultRecoveryCoordinator
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Dictionary<FaultRecoveryScenario, FaultRecoveryState> _states = new();
    private readonly object _gate = new();

    public FaultRecoveryCoordinator(IMediaForgeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics;

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

    public async Task<FaultRecoveryState> HandleFaultAsync(
        FaultRecoveryScenario scenario,
        string detail,
        Func<CancellationToken, Task<bool>> recoveryAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryAction);

        var attempt = 0;
        var backoff = InitialBackoff;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            var state = CreateState(scenario, detail, attempt);
            PublishState(state);

            try
            {
                if (await recoveryAction(cancellationToken).ConfigureAwait(false))
                {
                    ClearState(scenario);
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Info,
                        "engine.fault_recovery_succeeded",
                        $"Recovered from {scenario}.",
                        nameof(FaultRecoveryCoordinator));
                    return new FaultRecoveryState
                    {
                        Scenario = state.Scenario,
                        Detail = $"{detail} (recovered)",
                        AttemptCount = state.AttemptCount,
                        LastAttemptUtc = state.LastAttemptUtc,
                        RequiresRecordingPause = state.RequiresRecordingPause,
                        RequiresStreamingPause = state.RequiresStreamingPause
                    };
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

            if (attempt >= 5)
                break;

            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, MaxBackoff.TotalMilliseconds));
        }

        var failedState = CreateState(scenario, $"{detail} (recovery exhausted)", attempt);
        PublishState(failedState);
        return failedState;
    }

    public void NotifyEncoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.EncoderUnavailable, detail, 1));

    public void NotifyDecoderUnavailable(string detail) =>
        PublishState(CreateState(FaultRecoveryScenario.DecoderUnavailable, detail, 1));

    public void ClearState(FaultRecoveryScenario scenario)
    {
        lock (_gate)
            _states.Remove(scenario);
    }

    private FaultRecoveryState CreateState(FaultRecoveryScenario scenario, string detail, int attempt) =>
        scenario switch
        {
            FaultRecoveryScenario.EncoderUnavailable => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt,
                RequiresRecordingPause = true,
                RequiresStreamingPause = true
            },
            FaultRecoveryScenario.DecoderUnavailable => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt
            },
            FaultRecoveryScenario.VulkanDeviceLost => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt
            },
            FaultRecoveryScenario.MonitorDisconnected => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt
            },
            FaultRecoveryScenario.WebcamRemoved => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt
            },
            FaultRecoveryScenario.GpuSwitch => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt,
                RequiresRecordingPause = true,
                RequiresStreamingPause = true
            },
            _ => new FaultRecoveryState
            {
                Scenario = scenario,
                Detail = detail,
                AttemptCount = attempt
            }
        };

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
