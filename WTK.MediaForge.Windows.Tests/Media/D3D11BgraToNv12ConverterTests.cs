using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media.Encode;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class D3D11BgraToNv12ConverterTests
{
    [Fact]
    public async Task Bgra_to_nv12_converter_reports_unavailable_without_cpu_fallback()
    {
        var converter = new D3D11BgraToNv12Converter();
        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = 1920,
            Height = 1080,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };
        var requirement = new HardwareEncoderInputRequirement
        {
            Width = 1920,
            Height = 1080,
            PixelFormat = "NV12",
            RequiresGpuSurface = true
        };

        Assert.False(converter.CanConvert(descriptor, requirement));

        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        using var sourceTexture = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 1920,
            Height = 1080,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });
        var audit = new CollectingMediaTransportAuditSink();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await converter.ConvertAsync(sourceTexture, requirement, audit));

        Assert.Contains("D3D11 GPU conversion path", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionSucceeded));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
    }

    [Fact]
    [Trait("Category", "GPU")]
    public async Task Bgra_to_nv12_converter_uses_gpu_video_processor_or_reports_backend_unavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var dxgiFactory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        dxgiFactory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var converter = new D3D11BgraToNv12Converter(gpuDevice.Device);
        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = 320,
            Height = 180,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };
        var requirement = new HardwareEncoderInputRequirement
        {
            Width = 320,
            Height = 180,
            PixelFormat = "NV12",
            RequiresGpuSurface = true
        };

        Assert.True(converter.CanConvert(descriptor, requirement));

        using var pool = new GpuResourcePool(new D3D11SharedTextureTestFactory(gpuDevice.Device));
        using var sourceTexture = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 320,
            Height = 180,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });
        var audit = new CollectingMediaTransportAuditSink();

        try
        {
            using var converted = await converter.ConvertAsync(
                sourceTexture,
                requirement,
                audit,
                CancellationToken.None);

            Assert.Equal("NV12", converted.Descriptor.Format);
            Assert.Equal(MediaTransportKind.GpuSurface, converted.Descriptor.TransportKind);
            Assert.IsType<D3D11SharedTextureFrameHandle>(converted.BackendSurface);
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionStarted));
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionSucceeded));
            Assert.False(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
        }
        catch (NotSupportedException)
        {
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionStarted));
            Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
            Assert.False(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionSucceeded));
        }

        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
    }

    private sealed class FakeGpuTextureFactory : IGpuTextureFactory
    {
        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
        {
            _ = descriptor;
            return new FakePhysicalResource();
        }
    }

    private sealed class FakePhysicalResource : IGpuPhysicalResource
    {
        public Task FullyDisposed => Task.CompletedTask;

        public bool TryFinalizePhysicalResources() => true;
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
