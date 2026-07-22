using System.Diagnostics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows;

internal sealed class ImageFileVideoFrameProvider : IVideoFrameProvider, IDisposable
{
    private const int KeyedMutexTimeoutMilliseconds = 1000;
    private readonly object _gate = new();
    private readonly ImageFileSourceRuntime _runtime;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private D3D11GpuDevice? _device;
    private D3D11SharedTextureFrameHandle? _sharedTexture;
    private int _activeLeases;
    private bool _disposeGpuResourcesWhenIdle;
    private bool _disposed;
    private long _frameNumber;

    public ImageFileVideoFrameProvider(
        SourceId id,
        string name,
        ImageFileSourceRuntime runtime,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _diagnostics = diagnostics;
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State => _runtime.State;

    public Exception? LastError { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        lock (_gate)
        {
            if (_sharedTexture is not null && _runtime.State == MediaSourceState.Running)
                return;

            if (_activeLeases > 0 && _disposeGpuResourcesWhenIdle)
            {
                throw new InvalidOperationException(
                    $"Static image source '{Name}' cannot restart while previous GPU frame leases are still active.");
            }

            _disposeGpuResourcesWhenIdle = false;
        }

        D3D11GpuDevice? device = null;
        D3D11SharedTextureFrameHandle? sharedTexture = null;

        try
        {
            await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            var asset = _runtime.LoadedAsset ??
                throw new InvalidOperationException("Static image runtime did not expose the loaded asset.");

            device = CreateDefaultDevice();
            sharedTexture = UploadStaticImage(device, asset);
            _runtime.MarkGpuUploaded(sharedTexture);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                DisposeGpuResourcesLocked();
                _device = device;
                _sharedTexture = sharedTexture;
                device = null;
                sharedTexture = null;
                LastError = null;
            }

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Info,
                "source.static_image_uploaded",
                $"Static image '{_runtime.Settings.Path}' uploaded to a D3D11 shared texture for source '{Name}'.",
                nameof(ImageFileVideoFrameProvider),
                sourceId: Id.Value,
                sourceName: Name);
        }
        catch (Exception ex)
        {
            LastError = ex;
            sharedTexture?.Dispose();
            device?.Dispose();

            try
            {
                await _runtime.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception stopEx)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.static_image_start_rollback_failed",
                    $"Static image source '{Name}' failed to stop after upload failure.",
                    nameof(ImageFileVideoFrameProvider),
                    stopEx,
                    Id.Value,
                    Name);
            }

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            _disposeGpuResourcesWhenIdle = true;
            if (_activeLeases == 0)
                DisposeGpuResourcesLocked();
        }

        return _runtime.StopAsync(cancellationToken);
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        lease = null!;
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        D3D11SharedTextureFrameHandle sharedTexture;
        FrameSize textureSize;
        long frameNumber;

        lock (_gate)
        {
            if (_runtime.State != MediaSourceState.Running ||
                _disposeGpuResourcesWhenIdle ||
                _sharedTexture is null)
            {
                return false;
            }

            _activeLeases++;
            sharedTexture = _sharedTexture;
            textureSize = sharedTexture.TextureSize;
            frameNumber = Interlocked.Increment(ref _frameNumber);
        }

        var frame = new GpuFrameReference
        {
            SourceId = Id,
            Backend = sharedTexture.Backend,
            Handle = sharedTexture,
            TextureSize = textureSize,
            LogicalSize = textureSize,
            PixelFormat = sharedTexture.Format.ToString(),
            FrameNumber = frameNumber,
            Timestamp = MediaTime.FromStopwatchTicks(Stopwatch.GetTimestamp())
        };

        lease = GpuFrameLease.Create(
            frame,
            ReleaseFrameLease,
            ex =>
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.static_image_lease_release_failed",
                    $"Static image source '{Name}' failed to release a GPU frame lease.",
                    nameof(ImageFileVideoFrameProvider),
                    ex,
                    Id.Value,
                    Name));
        return true;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _disposeGpuResourcesWhenIdle = true;
            if (_activeLeases == 0)
                DisposeGpuResourcesLocked();
        }

        _runtime.Dispose();
    }

    private bool IsDisposed
    {
        get
        {
            lock (_gate)
                return _disposed;
        }
    }

    private void ReleaseFrameLease()
    {
        lock (_gate)
        {
            if (_activeLeases <= 0)
                throw new InvalidOperationException("Static image source lease count underflow.");

            _activeLeases--;
            if (_activeLeases == 0 && _disposeGpuResourcesWhenIdle)
                DisposeGpuResourcesLocked();
        }
    }

    private void DisposeGpuResourcesLocked()
    {
        _sharedTexture?.Dispose();
        _sharedTexture = null;
        _device?.Dispose();
        _device = null;
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

    private static D3D11SharedTextureFrameHandle UploadStaticImage(
        D3D11GpuDevice device,
        StaticCpuAsset asset)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.PixelFormat != RenderPixelFormat.Rgba8Unorm)
        {
            throw new NotSupportedException(
                $"Static image pixel format '{asset.PixelFormat}' is not supported for GPU upload.");
        }

        var width = checked((int)asset.Size.Width);
        var height = checked((int)asset.Size.Height);
        var sourceRowPitch = checked(width * 4);
        var expectedByteLength = checked(sourceRowPitch * height);
        if (asset.Pixels.Length != expectedByteLength)
        {
            throw new InvalidOperationException(
                $"Static image '{asset.Path}' has {asset.Pixels.Length} bytes, expected {expectedByteLength}.");
        }

        var handle = D3D11SharedTextureFactory.CreateSharedTexture(
            device.Device,
            asset.Size.Width,
            asset.Size.Height,
            Format.B8G8R8A8_UNorm);

        var mutexAcquired = false;
        var uploadSucceeded = false;

        try
        {
            try
            {
                handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, KeyedMutexTimeoutMilliseconds);
                mutexAcquired = true;

                var stagingDescription = new Texture2DDescription
                {
                    Width = asset.Size.Width,
                    Height = asset.Size.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Write
                };

                using var staging = device.Device.CreateTexture2D(stagingDescription);
                var mapped = device.Context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);

                try
                {
                    CopyRgbaToMappedBgra(asset.Pixels, mapped, width, height, sourceRowPitch);
                }
                finally
                {
                    device.Context.Unmap(staging, 0);
                }

                device.Context.CopyResource(handle.Texture, staging);
                device.Context.Flush();
                uploadSucceeded = true;
            }
            finally
            {
                if (mutexAcquired)
                {
                    handle.KeyedMutex.ReleaseSync(
                        uploadSucceeded
                            ? D3D11SharedTextureSyncKeys.Consumer
                            : D3D11SharedTextureSyncKeys.Producer);

                    if (uploadSucceeded)
                        handle.NotifyCaptureReleasedToConsumer();
                }
            }

            if (uploadSucceeded)
                return handle;

            throw new InvalidOperationException($"Static image '{asset.Path}' was not uploaded to GPU memory.");
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static unsafe void CopyRgbaToMappedBgra(
        byte[] rgbaPixels,
        MappedSubresource mapped,
        int width,
        int height,
        int sourceRowPitch)
    {
        var destination = new Span<byte>(
            mapped.DataPointer.ToPointer(),
            checked((int)mapped.RowPitch * height));
        var destinationRowPitch = checked((int)mapped.RowPitch);

        for (var y = 0; y < height; y++)
        {
            var sourceRowOffset = checked(y * sourceRowPitch);
            var destinationRowOffset = checked(y * destinationRowPitch);

            for (var x = 0; x < width; x++)
            {
                var sourceOffset = sourceRowOffset + (x * 4);
                var destinationOffset = destinationRowOffset + (x * 4);

                destination[destinationOffset] = rgbaPixels[sourceOffset + 2];
                destination[destinationOffset + 1] = rgbaPixels[sourceOffset + 1];
                destination[destinationOffset + 2] = rgbaPixels[sourceOffset];
                destination[destinationOffset + 3] = rgbaPixels[sourceOffset + 3];
            }
        }
    }
}
