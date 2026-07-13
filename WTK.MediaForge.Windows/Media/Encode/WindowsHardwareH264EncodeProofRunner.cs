using Vortice.DXGI;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class WindowsHardwareH264EncodeProofRunner : HardwareMediaProofRunner
{
    private const int ProofWidth = 320;
    private const int ProofHeight = 180;
    private const int ProofFrameRate = 30;
    private const int MaxInputFrames = 8;

    public WindowsHardwareH264EncodeProofRunner()
        : base(MediaForgeCapabilityCatalog.HardwareEncodeProof, "Windows hardware H.264 encode proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Unavailable("Windows Media Foundation hardware encode proof requires Windows.", "MediaFoundation-HardwareMft");

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            using (adapter)
            using (var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter))
            await using (var encoder = new MediaFoundationHardwareVideoEncoder(
                gpuDevice.Device,
                new HardwareVideoEncoderSettings
                {
                    Width = ProofWidth,
                    Height = ProofHeight,
                    FramesPerSecond = ProofFrameRate,
                    BitrateBitsPerSecond = 2_000_000,
                    KeyFrameIntervalFrames = ProofFrameRate,
                    PixelFormat = "NV12"
                }))
            {
                var audit = new CollectingMediaTransportAuditSink();
                for (var frame = 0; frame < MaxInputFrames; frame++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var surface = D3D11SharedTextureFactory.CreateSharedTexture(
                        gpuDevice.Device,
                        ProofWidth,
                        ProofHeight,
                        Vortice.DXGI.Format.NV12);

                    using var inputLease = HardwareEncoderInputLease.CreateWithBackendSurface(
                        new GpuVideoFrameDescriptor
                        {
                            Width = ProofWidth,
                            Height = ProofHeight,
                            Format = "NV12",
                            TransportKind = MediaTransportKind.GpuSurface
                        },
                        surface);

                    var packet = await encoder.EncodeAsync(
                            new EncodeFrameContext
                            {
                                InputLease = inputLease,
                                FrameNumber = frame + 1,
                                PresentationTime = TimeSpan.FromSeconds(frame / (double)ProofFrameRate),
                                CancellationToken = cancellationToken
                            },
                            audit)
                        .ConfigureAwait(false);

                    if (packet is null)
                        continue;

                    if (packet.Data.IsEmpty ||
                        packet.Codec != EncodedVideoCodec.H264 ||
                        packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
                    {
                        return Unavailable(
                            "Media Foundation hardware encoder returned a packet without backend-output-validated H.264 evidence.",
                            "MediaFoundation-HardwareMft",
                            baseline.GpuVendor);
                    }

                    return Passed(
                        "MediaFoundation-HardwareMft",
                        ["BackendOutputValidated", "D3D11SurfaceInput", "H264Packet"],
                        baseline.GpuVendor,
                        $"Hardware encoder produced {packet.Data.Length} bytes.");
                }

                return Unavailable(
                    "Media Foundation hardware encoder accepted input but did not emit a packet within the proof frame budget.",
                    "MediaFoundation-HardwareMft",
                    baseline.GpuVendor);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Media Foundation hardware H.264 encode proof unavailable: {ex.Message}",
                "MediaFoundation-HardwareMft",
                baseline.GpuVendor);
        }
    }
}
