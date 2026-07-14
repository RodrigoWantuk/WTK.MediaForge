using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Reflection;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[Trait("Category", "GPU")]
public sealed class HardwareEncodeFoundationTests
{
    [Fact]
    public void Media_foundation_runtime_reference_count_allows_overlapping_leases()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var baseline = MediaFoundationRuntime.ReferenceCountForTests;
        using (MediaFoundationRuntime.Acquire())
        {
            Assert.True(MediaFoundationRuntime.ReferenceCountForTests >= baseline + 1);
            using (MediaFoundationRuntime.Acquire())
            {
                Assert.True(MediaFoundationRuntime.ReferenceCountForTests >= baseline + 2);
            }

            Assert.True(MediaFoundationRuntime.ReferenceCountForTests >= baseline + 1);
        }

        Assert.True(MediaFoundationRuntime.ReferenceCountForTests >= baseline);
    }

    [Fact]
    public async Task Windows_hardware_h264_encode_proof_returns_passed_or_unavailable_without_throwing()
    {
        var baseline = new HardwareMediaCapabilityReport
        {
            Platform = OperatingSystem.IsWindows() ? "Windows" : "Non-Windows",
            GpuVendor = "TestVendor"
        };
        var result = await new WindowsHardwareH264EncodeProofRunner()
            .RunAsync(baseline, CancellationToken.None);

        Assert.Equal(MediaForgeCapabilityCatalog.HardwareEncodeProof, result.Id);
        Assert.True(
            result.Status is HardwareMediaProofStatus.Passed or HardwareMediaProofStatus.Unavailable,
            $"Unexpected proof status: {result.Status}");

        if (result.Status == HardwareMediaProofStatus.Passed)
        {
            Assert.NotEmpty(result.Evidence);
            Assert.Contains(nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated), result.Evidence);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
    }

    [Fact]
    public async Task Hardware_encoder_settings_constructor_preserves_input_requirement()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var settings = new HardwareVideoEncoderSettings
        {
            Codec = EncodedVideoCodec.H264,
            Width = 1280,
            Height = 720,
            FramesPerSecond = 30,
            BitrateBitsPerSecond = 6_000_000,
            KeyFrameIntervalFrames = 60,
            PixelFormat = "NV12"
        };

        await using var encoder = new MediaFoundationHardwareVideoEncoder(gpuDevice.Device, settings);

        Assert.Equal(1280, encoder.InputRequirement.Width);
        Assert.Equal(720, encoder.InputRequirement.Height);
        Assert.Equal("NV12", encoder.InputRequirement.PixelFormat);
        Assert.True(encoder.InputRequirement.RequiresGpuSurface);
    }

    [Fact]
    public async Task Public_default_encoder_constructor_keeps_owned_d3d11_device_alive_until_dispose()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var encoder = new MediaFoundationHardwareVideoEncoder(320, 180);

        var ownedDeviceField = typeof(MediaFoundationHardwareVideoEncoder).GetField(
            "_ownedDevice",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var ownedDevice = Assert.IsType<OwnedD3D11EncoderDevice>(ownedDeviceField?.GetValue(encoder));

        Assert.NotNull(ownedDevice.Device);

        await encoder.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => ownedDevice.Device);
    }

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
    public async Task Encoder_uses_gpu_format_converter_when_exporter_cannot_match_encoder_format()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var audit = new CollectingMediaTransportAuditSink();
        var exporter = new RejectingFrameExporter();
        var converter = new D3D11BgraToNv12Converter(gpuDevice.Device);
        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width: 320,
            height: 180,
            pixelFormat: "NV12",
            allowPrototypeEncoding: true,
            formatConverter: converter);

        using var pool = new GpuResourcePool(new D3D11SharedTextureTestFactory(gpuDevice.Device));
        using var textureLease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 320,
            Height = 180,
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
            _ = await encoder.SubmitFrameAsync(textureLease, context, exporter, audit);
        }
        catch (InvalidOperationException)
        {
            // Prototype encoder availability varies by test host. The converter decision must already be visible.
        }
        catch (NotSupportedException)
        {
            // Some adapters expose the device but not the required VideoProcessor conversion.
        }

        Assert.True(exporter.CanExportCalled);
        Assert.False(exporter.ExportCalled);
        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionStarted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));

        if (audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionSucceeded))
        {
            Assert.True(audit.Contains(MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated));
            Assert.False(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
        }
        else
        {
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
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

    private sealed class RejectingFrameExporter : IGpuFrameExporter
    {
        public bool CanExportCalled { get; private set; }

        public bool ExportCalled { get; private set; }

        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement)
        {
            _ = descriptor;
            _ = requirement;
            CanExportCalled = true;
            return false;
        }

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default)
        {
            _ = descriptor;
            _ = auditSink;
            cancellationToken.ThrowIfCancellationRequested();
            ExportCalled = true;
            throw new InvalidOperationException("Exporter should not be used when CanExport returned false.");
        }
    }

    private sealed class D3D11SharedTextureTestFactory(ID3D11Device device) : IGpuTextureFactory
    {
        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor) =>
            new D3D11SharedPhysicalResource(D3D11SharedTextureFactory.CreateSharedTexture(
                device,
                (uint)descriptor.Width,
                (uint)descriptor.Height,
                ToDxgiFormat(descriptor.Format)));

        private static Format ToDxgiFormat(string format) =>
            format.Equals("NV12", StringComparison.OrdinalIgnoreCase)
                ? Format.NV12
                : Format.B8G8R8A8_UNorm;
    }

    private sealed class D3D11SharedPhysicalResource(D3D11SharedTextureFrameHandle handle)
        : IGpuPhysicalResource, IGpuFrameHandleProvider
    {
        private readonly TaskCompletionSource _fullyDisposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finalized;

        public Task FullyDisposed => _fullyDisposed.Task;

        public IGpuFrameHandle FrameHandle => handle;

        public bool TryFinalizePhysicalResources()
        {
            if (Interlocked.Exchange(ref _finalized, 1) != 0)
                return _fullyDisposed.Task.IsCompleted;

            handle.Dispose();
            _fullyDisposed.TrySetResult();
            return true;
        }
    }
}
