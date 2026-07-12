namespace WTK.MediaForge.Core.Media;

public sealed class HardwareMediaProofResult
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required HardwareMediaProofStatus Status { get; init; }

    public string? Backend { get; init; }

    public string? Vendor { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public HardwareMediaProof ToProof() =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            Status = Status,
            Backend = Backend,
            Vendor = Vendor,
            Reason = Reason,
            Evidence = Evidence
        };
}

public abstract class HardwareMediaProofRunner
{
    protected HardwareMediaProofRunner(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public abstract ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default);

    protected HardwareMediaProofResult Unavailable(
        string reason,
        string? backend = null,
        string? vendor = null) =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            Status = HardwareMediaProofStatus.Unavailable,
            Backend = backend,
            Vendor = vendor,
            Reason = string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("Unavailable proof result requires a reason.", nameof(reason))
                : reason
        };

    protected HardwareMediaProofResult Passed(
        string backend,
        IReadOnlyList<string> evidence,
        string? vendor = null,
        string? reason = null) =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            Status = HardwareMediaProofStatus.Passed,
            Backend = string.IsNullOrWhiteSpace(backend)
                ? throw new ArgumentException("Passed proof result requires a backend.", nameof(backend))
                : backend,
            Vendor = vendor,
            Evidence = (evidence ?? throw new ArgumentNullException(nameof(evidence))).Count == 0
                ? throw new ArgumentException("Passed proof result requires evidence.", nameof(evidence))
                : evidence,
            Reason = reason
        };
}

public sealed class HardwareMediaProofRegistry
{
    private readonly Dictionary<string, HardwareMediaProofRunner> _runners =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<HardwareMediaProofRunner> Runners => _runners.Values.ToArray();

    public void Register(HardwareMediaProofRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (!_runners.TryAdd(runner.Id, runner))
            throw new InvalidOperationException($"Hardware media proof runner '{runner.Id}' is already registered.");
    }

    public async ValueTask<IReadOnlyList<HardwareMediaProofResult>> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var results = new List<HardwareMediaProofResult>(_runners.Count);
        foreach (var runner in _runners.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await runner.RunAsync(baseline, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public static HardwareMediaCapabilityReport ApplyResults(
        HardwareMediaCapabilityReport baseline,
        IEnumerable<HardwareMediaProofResult> results)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(results);

        var merged = new Dictionary<string, HardwareMediaProof>(StringComparer.OrdinalIgnoreCase);
        foreach (var proof in baseline.Proofs)
            merged[proof.Id] = proof;

        foreach (var result in results)
            merged[result.Id] = result.ToProof();

        return new HardwareMediaCapabilityReport
        {
            Platform = baseline.Platform,
            GpuVendor = baseline.GpuVendor,
            DeviceName = baseline.DeviceName,
            DriverVersion = baseline.DriverVersion,
            DetectedApis = baseline.DetectedApis,
            HardwareDecodeCodecs = baseline.HardwareDecodeCodecs,
            HardwareEncodeCodecs = baseline.HardwareEncodeCodecs,
            BackendCapabilities = baseline.BackendCapabilities,
            Proofs = merged.Values.ToArray(),
            AcceptsGpuSurfaceInput = baseline.AcceptsGpuSurfaceInput,
            RequiresCpuStaging = baseline.RequiresCpuStaging,
            ExportProofStatus = baseline.ExportProofStatus,
            ExportProofReason = baseline.ExportProofReason
        };
    }
}
