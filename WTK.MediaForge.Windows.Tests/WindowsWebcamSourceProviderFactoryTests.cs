using Vortice.DXGI;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

public sealed class WindowsWebcamSourceProviderFactoryTests
{
    [Fact]
    public void Webcam_factory_creates_real_provider()
    {
        var factory = new WindowsWebcamSourceProviderFactory(
            sessionFactory: new FakeWebcamCaptureSessionFactory());

        Assert.True(factory.CanCreate(MediaSourceTypes.Webcam));

        using var provider = Assert.IsType<WindowsWebcamVideoFrameProvider>(
            factory.CreateProvider(CreateSourceDefinition(SourceId.New())));

        Assert.Equal(MediaSourceState.Stopped, provider.State);
    }

    [Fact]
    public async Task Webcam_provider_publishes_gpu_frame_with_keep_latest_lifetime()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sourceId = SourceId.New();
        var factory = new WindowsWebcamSourceProviderFactory(
            sessionFactory: new FakeWebcamCaptureSessionFactory());
        await using var provider = (WindowsWebcamVideoFrameProvider)factory.CreateProvider(
            CreateSourceDefinition(sourceId));

        await provider.StartAsync(CancellationToken.None);
        Assert.Equal(MediaSourceState.Running, provider.State);

        using var lease = await WaitForFrameAsync(provider);
        Assert.Equal(sourceId, lease.Frame.SourceId);
        Assert.Equal(GpuFrameBackend.D3D11SharedTexture, lease.Frame.Backend);
        Assert.Equal(new FrameSize(64, 36), lease.Frame.TextureSize);
        Assert.Equal(1, provider.ActiveSlotRetainCount);

        lease.Dispose();
        Assert.Equal(0, provider.ActiveSlotRetainCount);

        await provider.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSourceState.Stopped, provider.State);
    }

    [Fact]
    public async Task Webcam_provider_start_failure_is_observable()
    {
        var provider = new WindowsWebcamVideoFrameProvider(
            SourceId.New(),
            "Camera",
            new WebcamSourceSettings { DeviceId = "camera-1" },
            sessionFactory: new ThrowingWebcamCaptureSessionFactory());

        await using (provider)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.StartAsync(CancellationToken.None));

            Assert.Contains("fake webcam start failure", ex.Message, StringComparison.Ordinal);
            Assert.Equal(MediaSourceState.Failed, provider.State);
            Assert.Same(ex, provider.LastError);
        }
    }

    private static MediaForgeSourceDefinition CreateSourceDefinition(SourceId sourceId) =>
        new()
        {
            Id = sourceId,
            Name = "Camera",
            TypeId = MediaSourceTypes.Webcam,
            Settings = MediaSourceSettingsSerializer.ToJson(new WebcamSourceSettings
            {
                DeviceId = "camera-1",
                PreferredWidth = 64,
                PreferredHeight = 36,
                PreferredFrameRate = 30
            })
        };

    private static async Task<GpuFrameLease> WaitForFrameAsync(IVideoFrameProvider provider)
    {
        var deadline = Environment.TickCount64 + (long)TimeSpan.FromSeconds(2).TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (provider.TryAcquireLatestFrame(out var lease))
                return lease;

            if (provider.State == MediaSourceState.Failed)
                throw new InvalidOperationException("Provider failed before publishing a frame.", provider.LastError);

            await Task.Delay(5);
        }

        throw new TimeoutException("Webcam provider did not publish a frame before the timeout.");
    }

    private sealed class FakeWebcamCaptureSessionFactory : IWindowsWebcamCaptureSessionFactory
    {
        public IWindowsWebcamCaptureSession Create() => new FakeWebcamCaptureSession();
    }

    private sealed class ThrowingWebcamCaptureSessionFactory : IWindowsWebcamCaptureSessionFactory
    {
        public IWindowsWebcamCaptureSession Create() => new ThrowingWebcamCaptureSession();
    }

    private sealed class FakeWebcamCaptureSession : IWindowsWebcamCaptureSession
    {
        private D3D11GpuDevice? _device;
        private int _disposed;

        public string DeviceName => "Fake Camera";

        public FrameSize FrameSize { get; } = new(64, 36);

        public TimeSpan FrameDuration { get; } = TimeSpan.FromMilliseconds(33);

        public D3D11GpuDevice Device =>
            _device ?? throw new InvalidOperationException("Fake webcam session is not started.");

        public void Start(WebcamSourceSettings settings)
        {
            _ = settings;
            _device = CreateDefaultDevice();
        }

        public bool TryCaptureNextFrameTo(
            D3D11SharedTextureFrameHandle destination,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.KeyedMutex.AcquireSync(destination.ProducerAcquireKey, 1000);
            destination.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
            destination.NotifyCaptureReleasedToConsumer();
            return true;
        }

        public void RequestStop()
        {
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _device?.Dispose();
        }

        private static D3D11GpuDevice CreateDefaultDevice()
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();

            try
            {
                return D3D11GpuDevice.CreateForAdapter(adapter);
            }
            catch
            {
                adapter?.Dispose();
                throw;
            }
        }
    }

    private sealed class ThrowingWebcamCaptureSession : IWindowsWebcamCaptureSession
    {
        public string DeviceName => "Throwing";

        public FrameSize FrameSize => default;

        public TimeSpan FrameDuration => TimeSpan.Zero;

        public D3D11GpuDevice Device => throw new InvalidOperationException("fake webcam start failure");

        public void Start(WebcamSourceSettings settings) =>
            throw new InvalidOperationException("fake webcam start failure");

        public bool TryCaptureNextFrameTo(
            D3D11SharedTextureFrameHandle destination,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fake webcam start failure");

        public void RequestStop()
        {
        }

        public void Dispose()
        {
        }
    }
}
