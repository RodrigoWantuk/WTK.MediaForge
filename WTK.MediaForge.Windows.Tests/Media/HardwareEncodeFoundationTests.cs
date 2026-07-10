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
    public async Task Public_encoder_rejects_prototype_canned_packet_path()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width: 640,
            height: 360);

        using var inputLease = HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
        {
            Width = 640,
            Height = 360,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        });

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await encoder.EncodeAsync(
                new EncodeFrameContext
                {
                    InputLease = inputLease,
                    FrameNumber = 1,
                    PresentationTime = TimeSpan.Zero,
                    CancellationToken = CancellationToken.None
                },
                new CollectingMediaTransportAuditSink()));

        Assert.Contains("Real Media Foundation H.264 hardware encoder output is unavailable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Public_submit_frame_rejects_before_exporting_gpu_surface_when_real_backend_is_unavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width: 320,
            height: 180);

        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        using var textureLease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 320,
            Height = 180,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });

        var exporter = new RecordingFrameExporter();
        var audit = new CollectingMediaTransportAuditSink();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await encoder.SubmitFrameAsync(
                textureLease,
                new HardwareEncodeFrameContext
                {
                    FrameId = 1,
                    PresentationTime = TimeSpan.Zero,
                    FrameBudget = TimeSpan.FromMilliseconds(33),
                    CancellationToken = CancellationToken.None
                },
                exporter,
                audit));

        Assert.Contains("prototype canned-packet bridge is not a product encoder backend", ex.Message, StringComparison.Ordinal);
        Assert.False(exporter.CanExportCalled);
        Assert.False(exporter.ExportCalled);
        Assert.Empty(audit.Events);
    }

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
            height: 360,
            pixelFormat: "B8G8R8A8_UNORM",
            allowPrototypeEncoding: true);

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
            height: 180,
            pixelFormat: "B8G8R8A8_UNORM",
            allowPrototypeEncoding: true);

        var exporter = new VulkanToD3D11EncoderSurfaceExporter(gpuDevice.Device);
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());

        await using var encodeTarget = new EncodeSchedulerTarget(
            encoder,
            exporter,
            audit,
            onPacketProduced: packets.Add);

        encodeTarget.OnRenderedFrame(new ScheduledRenderedFrame
        {
            Context = new FrameExecutionContext
            {
                FrameId = 1,
                FrameBudget = TimeSpan.FromMilliseconds(33),
                TargetOutputs = []
            },
            TextureLease = pool.AcquireTexture(new GpuTextureDescriptor
            {
                Width = 320,
                Height = 180,
                Format = "B8G8R8A8_UNORM",
                Usage = GpuTextureUsage.OffscreenColor
            })
        });

        await WaitForConditionAsync(
            () => audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded),
            TimeSpan.FromSeconds(2));
        await encodeTarget.DisposeAsync();

        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
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

    private sealed class RecordingFrameExporter : IGpuFrameExporter
    {
        public bool CanExportCalled { get; private set; }

        public bool ExportCalled { get; private set; }

        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement)
        {
            _ = descriptor;
            _ = requirement;
            CanExportCalled = true;
            return true;
        }

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default)
        {
            _ = auditSink;
            cancellationToken.ThrowIfCancellationRequested();
            ExportCalled = true;
            return ValueTask.FromResult(HardwareEncoderInputLease.Create(descriptor));
        }
    }
}
