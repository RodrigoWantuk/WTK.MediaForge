using WTK.MediaForge.Remote;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class RemoteSceneQualificationTests
{
    [Fact]
    public void Empty_evidence_keeps_gate_closed()
    {
        var result = RemoteSceneQualificationGate.Evaluate(Array.Empty<RemoteSceneQualificationEvidence>());
        Assert.False(result.IsQualified);
        Assert.Contains(result.MissingEvidence, static reason => reason.Contains("Direct report", StringComparison.Ordinal));
        Assert.Contains(result.MissingEvidence, static reason => reason.Contains("TURN report", StringComparison.Ordinal));
    }

    [Fact]
    public void Direct_and_turn_require_all_physical_scenarios_and_clean_shutdown()
    {
        var all = Enum.GetValues<RemoteSceneQualificationScenario>().ToHashSet();
        var result = RemoteSceneQualificationGate.Evaluate([
            CreateEvidence(RemoteSceneQualifiedPath.Direct, all),
            CreateEvidence(RemoteSceneQualifiedPath.TurnRelay, all)]);
        Assert.True(result.IsQualified, result.CapabilityReason);
        Assert.Empty(result.MissingEvidence);
    }

    [Fact]
    public void Cpu_raw_path_or_outstanding_lease_blocks_qualification()
    {
        var scenarios = Enum.GetValues<RemoteSceneQualificationScenario>().ToHashSet();
        var direct = CreateEvidence(RemoteSceneQualifiedPath.Direct, scenarios) with { RawCpuVideoPathObserved = true };
        var turn = CreateEvidence(RemoteSceneQualifiedPath.TurnRelay, scenarios) with
        {
            Resources = new RemoteSceneResourceEvidence(
                new(1, 1, 10, 0, 0), new(2, 2, 20, 8, 8), new(1, 1, 10, 0, 1), true)
        };
        var result = RemoteSceneQualificationGate.Evaluate([direct, turn]);
        Assert.False(result.IsQualified);
        Assert.Contains(result.MissingEvidence, static reason => reason.Contains("raw CPU", StringComparison.Ordinal));
        Assert.Contains(result.MissingEvidence, static reason => reason.Contains("baseline", StringComparison.Ordinal));
    }

    private static RemoteSceneQualificationEvidence CreateEvidence(RemoteSceneQualifiedPath path, IReadOnlySet<RemoteSceneQualificationScenario> scenarios) => new()
    {
        EvidenceId = $"physical-{path}", CapturedAt = DateTimeOffset.UtcNow,
        SustainedDuration = TimeSpan.FromMinutes(30), Path = path,
        PublisherAdapter = "adapter-a", SubscriberAdapter = "adapter-b",
        HardwareEncoder = "hardware-h264-encoder", HardwareDecoder = "hardware-h264-decoder",
        SelectedIceCandidate = path == RemoteSceneQualifiedPath.Direct ? "host/udp" : "relay/udp",
        TurnServer = path == RemoteSceneQualifiedPath.TurnRelay ? new Uri("turns://turn.invalid") : null,
        RoundTripTime = TimeSpan.FromMilliseconds(20), PacketLossPercent = 0.1,
        Jitter = TimeSpan.FromMilliseconds(2), BitrateBitsPerSecond = 4_000_000,
        FramesSent = 54_000, FramesReceived = 53_990, KeyFrames = 60, ReconnectCount = 1,
        Resources = new(new(100, 100, 10, 0, 0), new(200, 200, 20, 8, 8), new(100, 100, 10, 0, 0), true),
        Scenarios = scenarios, RawCpuVideoPathObserved = false, DeterministicShutdownObserved = true
    };
}
