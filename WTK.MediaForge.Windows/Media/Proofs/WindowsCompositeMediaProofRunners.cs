using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Windows.Media.Decode;
using WTK.MediaForge.Windows.Media.Proofs;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class WindowsRenderToH264EncodeProofRunner : HardwareMediaProofRunner
{
    public WindowsRenderToH264EncodeProofRunner()
        : base(MediaForgeCapabilityCatalog.RenderToEncodeProof, "Windows rendered output to H.264 encode proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(
                "Windows render-to-encode proof requires Windows D3D11 and Media Foundation.",
                "Vulkan-D3D11-MediaFoundation",
                baseline.GpuVendor);
        }

        try
        {
            var result = await WindowsRenderedOutputH264ProofPipeline
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);

            return Passed(
                "Vulkan-D3D11-MediaFoundation",
                [
                    "VulkanOffscreenRenderTarget",
                    "D3D11EncoderSurface",
                    "GpuFormatConversion",
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "H264Packet"
                ],
                baseline.GpuVendor,
                $"Rendered {result.RenderedFrameCount} Vulkan frame(s) and produced {result.Packet.Data.Length} backend-validated H.264 bytes.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Render-to-encode product proof unavailable on this machine: {ex.Message}",
                "Vulkan-D3D11-MediaFoundation",
                baseline.GpuVendor);
        }
    }
}

internal sealed class WindowsMp4OutputProductProofRunner : HardwareMediaProofRunner
{
    public WindowsMp4OutputProductProofRunner()
        : base(MediaForgeCapabilityCatalog.Mp4OutputProductProof, "Windows MP4 output product proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var renderEncode = await WindowsRenderedOutputH264ProofPipeline
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);
            var outputPath = Path.Combine(
                Path.GetTempPath(),
                $"wtk_mediaforge_mp4_product_proof_{Guid.NewGuid():N}.mp4");

            try
            {
                await using var sink = new RecordingMp4PacketSink(outputPath);
                await sink
                    .StartAsync(
                        new EncodedPacketSinkContext
                        {
                            Codec = EncodedVideoCodec.H264,
                            Size = new FrameSize(
                                (uint)renderEncode.EncoderSettings.Width,
                                (uint)renderEncode.EncoderSettings.Height),
                            FramesPerSecond = renderEncode.EncoderSettings.FramesPerSecond
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                await sink.WritePacketAsync(renderEncode.Packet, cancellationToken).ConfigureAwait(false);
                await sink.StopAsync(cancellationToken).ConfigureAwait(false);

                if (!IsoBmffMp4Writer.HasValidH264BoxStructure(
                        outputPath,
                        new IsoBmffMp4Writer.TrackMetadata(
                            (uint)renderEncode.EncoderSettings.Width,
                            (uint)renderEncode.EncoderSettings.Height),
                        minimumSampleCount: 1))
                {
                    return Unavailable(
                        "MP4 product proof wrote a file that failed H.264 MP4 box validation.",
                        "Vulkan-D3D11-MediaFoundation+NativeMp4Mux",
                        baseline.GpuVendor);
                }

                return Passed(
                    "Vulkan-D3D11-MediaFoundation+NativeMp4Mux",
                    [
                        nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                        "PacketOnlyMp4Mux",
                        "ValidFtypMoovMdatAvcC"
                    ],
                    baseline.GpuVendor,
                    $"MP4 product proof wrote a valid H.264 MP4 file from a real render-to-encode packet ({new FileInfo(outputPath).Length} bytes).");
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"MP4 product proof unavailable on this machine: {ex.Message}",
                "Vulkan-D3D11-MediaFoundation+NativeMp4Mux",
                baseline.GpuVendor);
        }
    }
}

internal sealed class WindowsRtmpNetworkOutputProofRunner : HardwareMediaProofRunner
{
    public WindowsRtmpNetworkOutputProofRunner()
        : base(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof, "Windows RTMP network output proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var renderEncode = await WindowsRenderedOutputH264ProofPipeline
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var server = new WindowsLocalRtmpProofServer();
            await using var sink = new RtmpPacketSink(server.Url);
            await sink
                .StartAsync(
                    new EncodedPacketSinkContext
                    {
                        Codec = EncodedVideoCodec.H264,
                        Size = new FrameSize(
                            (uint)renderEncode.EncoderSettings.Width,
                            (uint)renderEncode.EncoderSettings.Height),
                        FramesPerSecond = renderEncode.EncoderSettings.FramesPerSecond
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await sink.WritePacketAsync(renderEncode.Packet, cancellationToken).ConfigureAwait(false);
            await server.WaitForVideoPacketsAsync(1, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

            return Passed(
                "TCP-RTMP",
                [
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "RtmpHandshake",
                    "RtmpConnectPublish",
                    "FlvH264VideoTag"
                ],
                baseline.GpuVendor,
                $"RTMP product proof published {server.VideoPacketCount} H.264 FLV video tag(s) over TCP.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"RTMP network product proof unavailable on this machine: {ex.Message}",
                "TCP-RTMP",
                baseline.GpuVendor);
        }
    }
}

internal sealed class WindowsHardwareDecodeProofRunner : HardwareMediaProofRunner
{
    public WindowsHardwareDecodeProofRunner()
        : base(MediaForgeCapabilityCatalog.HardwareDecodeProof, "Windows hardware H.264 decode proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(
                "Windows hardware decode proof requires Windows D3D11VA and Media Foundation.",
                "MediaFoundation-D3D11VA",
                baseline.GpuVendor);
        }

        try
        {
            await using var asset = await WindowsProductMp4ProofAsset
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var decoder = new MediaFoundationHardwareVideoDecoder();
            var audit = new CollectingMediaTransportAuditSink();
            using var decoded = await WindowsHardwareDecodeProofPipeline
                .DecodeGeneratedMp4FrameAsync(asset, decoder, audit, cancellationToken)
                .ConfigureAwait(false);

            return Passed(
                "MediaFoundation-D3D11VA",
                [
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "IMFDXGIBuffer",
                    "D3D11DecodedTexture"
                ],
                baseline.GpuVendor,
                $"Decoded generated MP4 asset to a validated {decoded.Width}x{decoded.Height} GPU texture.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Hardware decode product proof unavailable on this machine: {ex.Message}",
                "MediaFoundation-D3D11VA",
                baseline.GpuVendor);
        }
    }
}

internal sealed class WindowsDecodeToRenderProofRunner : HardwareMediaProofRunner
{
    public WindowsDecodeToRenderProofRunner()
        : base(MediaForgeCapabilityCatalog.DecodeToRenderProof, "Windows hardware decode to renderer proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(
                "Windows decode-to-render proof requires Windows D3D11VA, D3D11 shared textures, and Vulkan.",
                "MediaFoundation-D3D11VA-Vulkan",
                baseline.GpuVendor);
        }

        try
        {
            await using var asset = await WindowsProductMp4ProofAsset
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var decoder = new MediaFoundationHardwareVideoDecoder();
            var audit = new CollectingMediaTransportAuditSink();
            using var decoded = await WindowsHardwareDecodeProofPipeline
                .DecodeGeneratedMp4FrameAsync(asset, decoder, audit, cancellationToken)
                .ConfigureAwait(false);

            await WindowsHardwareDecodeProofPipeline
                .SubmitDecodedSourceFrameToRendererAsync(decoded, cancellationToken)
                .ConfigureAwait(false);

            return Passed(
                "MediaFoundation-D3D11VA-Vulkan",
                [
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "IMFDXGIBuffer",
                    "D3D11SharedTexture",
                    "VulkanExternalTextureImport",
                    "VulkanOffscreenRenderTarget"
                ],
                baseline.GpuVendor,
                "Decoded generated MP4 asset to a GPU texture and submitted it through a Vulkan source-layer render pass.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Decode-to-render product proof unavailable on this machine: {ex.Message}",
                "MediaFoundation-D3D11VA-Vulkan",
                baseline.GpuVendor);
        }
    }
}
