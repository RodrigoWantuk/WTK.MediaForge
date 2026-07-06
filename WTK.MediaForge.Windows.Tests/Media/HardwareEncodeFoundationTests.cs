using Vortice.DXGI;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[Trait("Category", "GPU")]
public sealed class HardwareEncodeFoundationTests
{
    [Fact]
    public async Task Encoder_accepts_gpu_texture_lease_from_export_path()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var audit = new CollectingMediaTransportAuditSink();
        var exporter = new VulkanToD3D11EncoderSurfaceExporter(gpuDevice.Device);
        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width: 640,
            height: 360);

        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        using var textureLease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 640,
            Height = 360,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });

        var context = new HardwareEncodeFrameContext
        {
            FrameId = 1,
            PresentationTime = TimeSpan.Zero,
            FrameBudget = TimeSpan.FromMilliseconds(33),
            CancellationToken = CancellationToken.None
        };

        try
        {
            var packet = await encoder.SubmitFrameAsync(textureLease, context, exporter, audit);
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
            Assert.True(audit.Contains(MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated));

            if (packet is not null)
            {
                Assert.Equal(EncodedVideoCodec.H264, packet.Codec);
                Assert.True(H264NalUtilities.ContainsValidStartCode(packet.Data.Span));
            }
        }
        catch (InvalidOperationException)
        {
            // Hardware encoder may be unavailable on CI; export path must still be exercised.
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
        }
    }

    [Fact]
    public async Task Scheduler_coordinates_render_and_encode_without_sink_render_call()
    {
        var audit = new CollectingMediaTransportAuditSink();
        var packets = new List<EncodedVideoPacket>();

        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width: 320,
            height: 180);

        var exporter = new VulkanToD3D11EncoderSurfaceExporter(gpuDevice.Device);
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());

        await using var encodeTarget = new EncodeSchedulerTarget(
            encoder,
            exporter,
            audit,
            acquireRenderedFrame: () => pool.AcquireTexture(new GpuTextureDescriptor
            {
                Width = 320,
                Height = 180,
                Format = "B8G8R8A8_UNORM",
                Usage = GpuTextureUsage.OffscreenColor
            }),
            onPacketProduced: packets.Add);

        encodeTarget.OnScheduledFrame(new FrameExecutionContext
        {
            FrameId = 1,
            FrameBudget = TimeSpan.FromMilliseconds(33),
            TargetOutputs = []
        });

        await Task.Delay(250);
        await encodeTarget.DisposeAsync();

        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
    }

    private sealed class FakeGpuTextureFactory : IGpuTextureFactory
    {
        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor) =>
            new FakePhysicalResource();

        private sealed class FakePhysicalResource : IGpuPhysicalResource
        {
            public Task FullyDisposed => Task.CompletedTask;

            public void Dispose()
            {
            }

            public bool TryFinalizePhysicalResources() => true;
        }
    }
}
