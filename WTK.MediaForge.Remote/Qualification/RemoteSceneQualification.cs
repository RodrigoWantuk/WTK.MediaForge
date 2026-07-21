namespace WTK.MediaForge.Remote;

public enum RemoteSceneQualificationScenario
{
    DirectConnection,
    TurnRelay,
    BothPeersBehindCgnat,
    PacketLoss,
    BitrateChange,
    KeyFrameRequest,
    DisconnectAndReconnect,
    AbruptShutdown,
    SimultaneousMp4,
    SimultaneousRtmp,
    NestedSceneSource,
    ApplyAndLiveEditing
}

public enum RemoteSceneQualifiedPath { Direct, TurnRelay }

public sealed record RemoteSceneResourceSnapshot(
    long RamBytes,
    long VramBytes,
    long HandleCount,
    long QueuedPackets,
    long OutstandingLeases);

public sealed record RemoteSceneResourceEvidence(
    RemoteSceneResourceSnapshot Baseline,
    RemoteSceneResourceSnapshot Peak,
    RemoteSceneResourceSnapshot Final,
    bool BaselineRestoredFromObservedPlateau)
{
    public bool HasNoOutstandingWork =>
        Final.QueuedPackets == 0 && Final.OutstandingLeases == 0 &&
        Final.HandleCount <= Baseline.HandleCount && BaselineRestoredFromObservedPlateau;
}

public sealed record RemoteSceneQualificationEvidence
{
    public required string EvidenceId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required TimeSpan SustainedDuration { get; init; }
    public required RemoteSceneQualifiedPath Path { get; init; }
    public required string PublisherAdapter { get; init; }
    public required string SubscriberAdapter { get; init; }
    public required string HardwareEncoder { get; init; }
    public required string HardwareDecoder { get; init; }
    public required string SelectedIceCandidate { get; init; }
    public Uri? TurnServer { get; init; }
    public required TimeSpan RoundTripTime { get; init; }
    public required double PacketLossPercent { get; init; }
    public required TimeSpan Jitter { get; init; }
    public required long BitrateBitsPerSecond { get; init; }
    public required long FramesSent { get; init; }
    public required long FramesReceived { get; init; }
    public required long KeyFrames { get; init; }
    public required long ReconnectCount { get; init; }
    public required RemoteSceneResourceEvidence Resources { get; init; }
    public required IReadOnlySet<RemoteSceneQualificationScenario> Scenarios { get; init; }
    public required bool RawCpuVideoPathObserved { get; init; }
    public required bool DeterministicShutdownObserved { get; init; }
}

public sealed record RemoteSceneQualificationResult(bool IsQualified, IReadOnlyList<string> MissingEvidence)
{
    public string CapabilityReason => IsQualified
        ? "Remote Scene Direct and TURN qualification evidence passed."
        : $"Remote Scene remains Unavailable: {string.Join("; ", MissingEvidence)}";
}

/// <summary>
/// Evaluates captured physical evidence. It never runs a synthetic transport or promotes
/// the capability catalog; product promotion requires reviewed physical reports.
/// </summary>
public static class RemoteSceneQualificationGate
{
    public static readonly TimeSpan MinimumSustainedDuration = TimeSpan.FromMinutes(30);

    public static RemoteSceneQualificationResult Evaluate(IReadOnlyCollection<RemoteSceneQualificationEvidence> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var missing = new List<string>();
        ValidateReport(reports.FirstOrDefault(static r => r.Path == RemoteSceneQualifiedPath.Direct), "Direct", false, missing);
        ValidateReport(reports.FirstOrDefault(static r => r.Path == RemoteSceneQualifiedPath.TurnRelay), "TURN", true, missing);

        var covered = reports.SelectMany(static r => r.Scenarios).ToHashSet();
        foreach (var scenario in Enum.GetValues<RemoteSceneQualificationScenario>())
        {
            if (!covered.Contains(scenario))
                missing.Add($"scenario {scenario} has no physical evidence");
        }

        return new RemoteSceneQualificationResult(missing.Count == 0, missing.AsReadOnly());
    }

    private static void ValidateReport(RemoteSceneQualificationEvidence? report, string label, bool requiresTurn, ICollection<string> missing)
    {
        if (report is null)
        {
            missing.Add($"{label} report is missing");
            return;
        }

        RequireText(report.EvidenceId, label, "evidence id", missing);
        RequireText(report.PublisherAdapter, label, "publisher adapter", missing);
        RequireText(report.SubscriberAdapter, label, "subscriber adapter", missing);
        RequireText(report.HardwareEncoder, label, "hardware encoder", missing);
        RequireText(report.HardwareDecoder, label, "hardware decoder", missing);
        RequireText(report.SelectedIceCandidate, label, "selected ICE candidate", missing);
        if (requiresTurn && report.TurnServer is null) missing.Add("TURN server evidence is missing");
        if (report.SustainedDuration < MinimumSustainedDuration) missing.Add($"{label} sustained duration is below {MinimumSustainedDuration}");
        if (report.RawCpuVideoPathObserved) missing.Add($"{label} observed an illegal raw CPU video path");
        if (!report.DeterministicShutdownObserved) missing.Add($"{label} deterministic shutdown was not observed");
        if (report.ReconnectCount < 1) missing.Add($"{label} reconnect was not observed");
        if (report.FramesSent <= 0 || report.FramesReceived <= 0 || report.KeyFrames <= 0) missing.Add($"{label} frame/keyframe counters are incomplete");
        if (report.BitrateBitsPerSecond <= 0 || report.RoundTripTime < TimeSpan.Zero || report.Jitter < TimeSpan.Zero || report.PacketLossPercent is < 0 or > 100)
            missing.Add($"{label} network telemetry is invalid");
        if (!report.Resources.HasNoOutstandingWork) missing.Add($"{label} resources did not return to baseline");
    }

    private static void RequireText(string value, string label, string field, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value)) missing.Add($"{label} {field} is missing");
    }
}
