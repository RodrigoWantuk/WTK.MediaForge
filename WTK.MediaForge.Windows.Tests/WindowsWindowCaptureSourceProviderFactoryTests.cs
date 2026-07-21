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

public sealed class WindowsWindowCaptureSourceProviderFactoryTests
{
    [Fact]
    public void Window_capture_factory_creates_gpu_provider()
    {
        var factory = new WindowsWindowCaptureSourceProviderFactory(
            sessionFactory: new FakeGraphicsCaptureSessionFactory());

        Assert.True(factory.CanCreate(MediaSourceTypes.WindowCapture));
        using var provider = Assert.IsType<WindowsWindowCaptureVideoFrameProvider>(
            factory.CreateProvider(CreateSourceDefinition(SourceId.New())));
        Assert.Equal(MediaSourceState.Stopped, provider.State);
    }

    [Fact]
    public void Window_capture_factory_rejects_empty_window_handle()
    {
        var factory = new WindowsWindowCaptureSourceProviderFactory(
            sessionFactory: new FakeGraphicsCaptureSessionFactory());
        var source = CreateSourceDefinition(SourceId.New(), windowHandle: 0);

        Assert.Throws<ArgumentException>(() => factory.CreateProvider(source));
    }

    [Fact]
    public async Task Window_capture_provider_publishes_gpu_frame_and_releases_lease()
    {
        var sourceId = SourceId.New();
        var factory = new WindowsWindowCaptureSourceProviderFactory(
            sessionFactory: new FakeGraphicsCaptureSessionFactory());
        await using var provider = (WindowsWindowCaptureVideoFrameProvider)factory.CreateProvider(
            CreateSourceDefinition(sourceId));

        await provider.StartAsync(CancellationToken.None);
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
    public async Task Window_capture_dispose_waits_for_retained_gpu_lease()
    {
        var provider = new WindowsWindowCaptureVideoFrameProvider(
            SourceId.New(),
            "Window",
            new WindowCaptureSourceSettings { WindowHandle = 123 },
            sessionFactory: new FakeGraphicsCaptureSessionFactory());

        await provider.StartAsync(CancellationToken.None);
        var lease = await WaitForFrameAsync(provider);
        await provider.StopAsync(CancellationToken.None);

        var disposeTask = provider.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        lease.Dispose();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Window_capture_start_failure_is_observable()
    {
        var provider = new WindowsWindowCaptureVideoFrameProvider(
            SourceId.New(),
            "Window",
            new WindowCaptureSourceSettings { WindowHandle = 123 },
            sessionFactory: new ThrowingGraphicsCaptureSessionFactory());

        await using (provider)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.StartAsync(CancellationToken.None));

            Assert.Contains("fake window capture start failure", exception.Message, StringComparison.Ordinal);
            Assert.Equal(MediaSourceState.Failed, provider.State);
            Assert.Same(exception, provider.LastError);
        }
    }

    private static MediaForgeSourceDefinition CreateSourceDefinition(
        SourceId sourceId,
        long windowHandle = 123) =>
        new()
        {
            Id = sourceId,
            Name = "Window",
            TypeId = MediaSourceTypes.WindowCapture,
            Settings = MediaSourceSettingsSerializer.ToJson(new WindowCaptureSourceSettings
            {
                WindowHandle = windowHandle,
                CaptureCursor = true
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

        if (provider.TryAcquireLatestFrame(out var finalLease))
            return finalLease;
        if (provider.State == MediaSourceState.Failed)
            throw new InvalidOperationException("Provider failed before publishing a frame.", provider.LastError);

        throw new TimeoutException("Window capture provider did not publish a frame before the timeout.");
    }

    private sealed class FakeGraphicsCaptureSessionFactory : IWindowsGraphicsCaptureSessionFactory
    {
        public IWindowsGraphicsCaptureSession Create() => new FakeGraphicsCaptureSession();
    }

    private sealed class ThrowingGraphicsCaptureSessionFactory : IWindowsGraphicsCaptureSessionFactory
    {
        public IWindowsGraphicsCaptureSession Create() => new ThrowingGraphicsCaptureSession();
    }

    private sealed class FakeGraphicsCaptureSession : IWindowsGraphicsCaptureSession
    {
        private D3D11GpuDevice? _device;
        private int _disposed;

        public string WindowTitle => "Fake Window";

        public FrameSize FrameSize { get; } = new(64, 36);

        public D3D11GpuDevice Device =>
            _device ?? throw new InvalidOperationException("Fake capture session is not started.");

        public void Start(WindowCaptureSourceSettings settings)
        {
            _ = settings;
            _device = WindowsD3D11AdapterSelector.CreateDevice(adapterAffinity: null);
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
    }

    private sealed class ThrowingGraphicsCaptureSession : IWindowsGraphicsCaptureSession
    {
        public string WindowTitle => "Throwing";
        public FrameSize FrameSize => default;
        public D3D11GpuDevice Device => throw new InvalidOperationException("fake window capture start failure");

        public void Start(WindowCaptureSourceSettings settings) =>
            throw new InvalidOperationException("fake window capture start failure");

        public bool TryCaptureNextFrameTo(
            D3D11SharedTextureFrameHandle destination,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fake window capture start failure");

        public void RequestStop()
        {
        }

        public void Dispose()
        {
        }
    }
}
