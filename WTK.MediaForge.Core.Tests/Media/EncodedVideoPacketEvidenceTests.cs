using System.Reflection;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class EncodedVideoPacketEvidenceTests
{
    [Fact]
    public void Encoded_packet_evidence_kind_is_observable_but_not_publicly_settable()
    {
        var evidenceKindProperty = typeof(EncodedVideoPacket)
            .GetProperty(nameof(EncodedVideoPacket.EvidenceKind));

        Assert.NotNull(evidenceKindProperty);
        Assert.Null(evidenceKindProperty!.SetMethod);
    }

    [Fact]
    public void Backend_validated_evidence_has_no_public_factory()
    {
        var publicFactories = typeof(EncodedVideoPacketEvidence)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method => method.Name.Contains("Validated", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Prototype", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(publicFactories);
    }

    [Fact]
    public void Internal_backend_validated_evidence_sets_observable_kind()
    {
        var packet = new EncodedVideoPacket
        {
            Data = new byte[] { 1, 2, 3 },
            Codec = EncodedVideoCodec.H264,
            Evidence = EncodedVideoPacketEvidence.CreateBackendOutputValidated(
                nameof(EncodedVideoPacketEvidenceTests),
                "TestBackend",
                MediaForgeCapabilityCatalog.HardwareEncodeProof)
        };

        Assert.Equal(MediaTransportAuditEvidenceKind.BackendOutputValidated, packet.EvidenceKind);
        Assert.Equal("TestBackend", packet.Evidence.Backend);
        Assert.Equal(MediaForgeCapabilityCatalog.HardwareEncodeProof, packet.Evidence.ProofId);
    }
}
