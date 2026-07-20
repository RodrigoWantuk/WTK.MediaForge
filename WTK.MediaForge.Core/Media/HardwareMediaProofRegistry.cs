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

        var renderToEncode = GetStatus(merged, MediaForgeCapabilityCatalog.RenderToEncodeProof);
        var hardwareEncode = GetStatus(merged, MediaForgeCapabilityCatalog.HardwareEncodeProof);
        var hardwareDecode = GetStatus(merged, MediaForgeCapabilityCatalog.HardwareDecodeProof);
        var exportProofStatus = renderToEncode switch
        {
            HardwareMediaProofStatus.Passed => GpuExportProofStatus.Passed,
            HardwareMediaProofStatus.Failed => GpuExportProofStatus.Failed,
            _ => baseline.ExportProofStatus
        };

        return new HardwareMediaCapabilityReport
        {
            Platform = baseline.Platform,
            GpuVendor = baseline.GpuVendor,
            DeviceName = baseline.DeviceName,
            DriverVersion = baseline.DriverVersion,
            AdapterId = baseline.AdapterId,
            DetectedApis = baseline.DetectedApis,
            HardwareDecodeCodecs = AddValidatedCodec(
                baseline.HardwareDecodeCodecs,
                hardwareDecode,
                "H264"),
            HardwareEncodeCodecs = AddValidatedCodec(
                baseline.HardwareEncodeCodecs,
                hardwareEncode,
                "H264"),
            BackendCapabilities = baseline.BackendCapabilities,
            Proofs = merged.Values.ToArray(),
            AcceptsGpuSurfaceInput = baseline.AcceptsGpuSurfaceInput ||
                hardwareEncode == HardwareMediaProofStatus.Passed,
            RequiresCpuStaging = baseline.RequiresCpuStaging,
            ExportProofStatus = exportProofStatus,
            ExportProofReason = exportProofStatus switch
            {
                GpuExportProofStatus.Passed => "Render-to-encode GPU export proof passed in this session.",
                GpuExportProofStatus.Failed => "Render-to-encode GPU export proof failed in this session.",
                _ => baseline.ExportProofReason
            }
        };
    }

    private static HardwareMediaProofStatus GetStatus(
        IReadOnlyDictionary<string, HardwareMediaProof> proofs,
        string proofId) =>
        proofs.TryGetValue(proofId, out var proof)
            ? proof.Status
            : HardwareMediaProofStatus.Pending;

    private static IReadOnlyList<string> AddValidatedCodec(
        IReadOnlyList<string> codecs,
        HardwareMediaProofStatus status,
        string codec) =>
        status == HardwareMediaProofStatus.Passed &&
        !codecs.Contains(codec, StringComparer.OrdinalIgnoreCase)
            ? [.. codecs, codec]
            : codecs;
}
