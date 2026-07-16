using System.Diagnostics;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media;

namespace WTK.MediaForge.Windows;

internal interface IWindowsWebcamCaptureSession : IDisposable
{
    string DeviceName { get; }

    FrameSize FrameSize { get; }

    TimeSpan FrameDuration { get; }

    D3D11GpuDevice Device { get; }

    void Start(WebcamSourceSettings settings);

    bool TryCaptureNextFrameTo(D3D11SharedTextureFrameHandle destination, CancellationToken cancellationToken);

    void RequestStop();
}

internal interface IWindowsWebcamCaptureSessionFactory
{
    IWindowsWebcamCaptureSession Create();
}

internal sealed class WindowsWebcamCaptureSessionFactory : IWindowsWebcamCaptureSessionFactory
{
    public IWindowsWebcamCaptureSession Create() => new WindowsWebcamCaptureSession();
}

internal sealed class WindowsWebcamCaptureSession : IWindowsWebcamCaptureSession
{
    private const int KeyedMutexTimeoutMilliseconds = 1000;

    private MediaFoundationRuntimeLease? _mediaFoundationRuntimeLease;
    private IMFMediaSource? _mediaSource;
    private IMFSourceReader? _sourceReader;
    private D3D11GpuDevice? _device;
    private ID3D11Texture2D? _stagingTexture;
    private int _sourceStride;
    private Guid _sourceSubtype = VideoFormatGuids.Rgb32;
    private bool _disposed;

    public string DeviceName { get; private set; } = "Webcam";

    public FrameSize FrameSize { get; private set; }

    public TimeSpan FrameDuration { get; private set; } = TimeSpan.FromMilliseconds(33);

    public D3D11GpuDevice Device =>
        _device ?? throw new InvalidOperationException("Webcam capture session has not been started.");

    public void Start(WebcamSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        DisposeSessionResources();
        _mediaFoundationRuntimeLease = MediaFoundationRuntime.Acquire();

        try
        {
            var resolved = WindowsWebcamDeviceEnumerator.Resolve(settings);
            DeviceName = resolved.FriendlyName;
            _device = CreateDefaultDevice();
            _mediaSource = ActivateMediaSource(resolved);

            using var readerAttributes = MediaFactory.MFCreateAttributes(5);
            readerAttributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true).CheckError();
            readerAttributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, false).CheckError();
            readerAttributes.Set(SourceReaderAttributeKeys.DisableCameraPlugins, false).CheckError();
            readerAttributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, true).CheckError();

            _sourceReader = MediaFactory.MFCreateSourceReaderFromMediaSource(_mediaSource, readerAttributes);
            _sourceReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            _sourceReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            var selected = SelectBestNativeMediaType(_sourceReader, settings);
            ConfigureOutput(_sourceReader, selected);

            using var currentType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            var frameSize = ReadFrameSize(currentType, selected.Width, selected.Height);
            var frameRate = ReadFrameRate(currentType, selected.FrameRate);
            _sourceSubtype = ReadSubtype(currentType, selected.Subtype);
            FrameSize = new FrameSize(checked((uint)frameSize.Width), checked((uint)frameSize.Height));
            FrameDuration = frameRate > 0
                ? TimeSpan.FromTicks(Math.Max(1, checked((long)(TimeSpan.TicksPerSecond / frameRate))))
                : TimeSpan.FromMilliseconds(33);
            _sourceStride = ReadStride(currentType, frameSize.Width, _sourceSubtype);
            _stagingTexture = CreateCpuUploadTexture(_device.Device, FrameSize.Width, FrameSize.Height);
        }
        catch
        {
            DisposeSessionResources();
            throw;
        }
    }

    public bool TryCaptureNextFrameTo(
        D3D11SharedTextureFrameHandle destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_sourceReader is null || _stagingTexture is null || _device is null)
            throw new InvalidOperationException("Webcam capture session is not open.");

        using var sample = _sourceReader.ReadSample(
            SourceReaderIndex.FirstVideoStream,
            SourceReaderControlFlag.None,
            out _,
            out var flags,
            out _);

        cancellationToken.ThrowIfCancellationRequested();

        if ((flags & SourceReaderFlag.Error) != 0)
            throw new InvalidOperationException("Media Foundation webcam SourceReader reported an error.");

        if ((flags & SourceReaderFlag.EndOfStream) != 0 || sample is null)
            return false;

        using var buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out var sourcePointer, out _, out var currentLength);
        try
        {
            CopySourceBufferToStagingTexture(sourcePointer, currentLength);
        }
        finally
        {
            buffer.Unlock();
        }

        var mutexAcquired = false;
        var uploadSucceeded = false;
        try
        {
            destination.KeyedMutex.AcquireSync(destination.ProducerAcquireKey, KeyedMutexTimeoutMilliseconds);
            mutexAcquired = true;
            _device.Context.CopyResource(destination.Texture, _stagingTexture);
            _device.Context.Flush();
            uploadSucceeded = true;
            return true;
        }
        finally
        {
            if (mutexAcquired)
            {
                destination.KeyedMutex.ReleaseSync(
                    uploadSucceeded
                        ? D3D11SharedTextureSyncKeys.Consumer
                        : D3D11SharedTextureSyncKeys.Producer);

                if (uploadSucceeded)
                    destination.NotifyCaptureReleasedToConsumer();
            }
        }
    }

    public void RequestStop()
    {
        try
        {
            _sourceReader?.Flush(SourceReaderIndex.FirstVideoStream);
        }
        catch
        {
            // Best-effort unblock only; Stop/Dispose reports the worker timeout if this fails.
        }

        try
        {
            _mediaSource?.Stop();
        }
        catch
        {
            // Best-effort unblock only; Stop/Dispose reports the worker timeout if this fails.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeSessionResources();
    }

    private static IMFMediaSource ActivateMediaSource(WindowsWebcamDeviceInfo device)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap)
            .CheckError();

        using var devices = MediaFactory.MFEnumDeviceSources(attributes);
        foreach (var activate in devices)
        {
            var symbolicLink = ReadString(activate, CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink);
            if (symbolicLink.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return activate.ActivateObject<IMFMediaSource>();
                }
                finally
                {
                    activate.Dispose();
                }
            }

            activate.Dispose();
        }

        throw new MediaForgeUnsupportedFeatureException(
            $"source.{MediaSourceTypes.Webcam.Value}",
            $"Media Foundation video capture device '{device.DeviceId}' disappeared before capture could start.");
    }

    private static WebcamNativeMediaType SelectBestNativeMediaType(
        IMFSourceReader reader,
        WebcamSourceSettings settings)
    {
        WebcamNativeMediaType? best = null;

        for (var index = 0; ; index++)
        {
            try
            {
                using var mediaType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, index);
                var (width, height) = ReadFrameSize(mediaType, settings.PreferredWidth ?? 1280, settings.PreferredHeight ?? 720);
                var frameRate = ReadFrameRate(mediaType, settings.PreferredFrameRate ?? 30);
                var subtype = ReadSubtype(mediaType, Guid.Empty);
                if (!IsSupportedNativeSubtype(subtype))
                    continue;

                var candidate = new WebcamNativeMediaType(width, height, frameRate, subtype);

                if (IsBetter(candidate, best, settings))
                    best = candidate;
            }
            catch (SharpGenException)
            {
                break;
            }
        }

        return best ?? new WebcamNativeMediaType(
            settings.PreferredWidth ?? 1280,
            settings.PreferredHeight ?? 720,
            settings.PreferredFrameRate ?? 30,
            VideoFormatGuids.Rgb32);
    }

    private static void ConfigureOutput(
        IMFSourceReader reader,
        WebcamNativeMediaType selected)
    {
        using var outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        outputType.Set(MediaTypeAttributeKeys.Subtype, selected.Subtype).CheckError();
        MediaFactory.MFSetAttributeSize(
            outputType,
            MediaTypeAttributeKeys.FrameSize,
            checked((uint)selected.Width),
            checked((uint)selected.Height)).CheckError();
        MediaFactory.MFSetAttributeRatio(
            outputType,
            MediaTypeAttributeKeys.FrameRate,
            checked((uint)Math.Max(1, Math.Round(selected.FrameRate))),
            1).CheckError();
        outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, outputType);
    }

    private static bool IsBetter(
        WebcamNativeMediaType candidate,
        WebcamNativeMediaType? current,
        WebcamSourceSettings settings)
    {
        if (current is null)
            return true;

        var preferredWidth = settings.PreferredWidth ?? 1280;
        var preferredHeight = settings.PreferredHeight ?? 720;
        var preferredFps = settings.PreferredFrameRate ?? 30;

        var candidateScore =
            Math.Abs(candidate.Width - preferredWidth) +
            Math.Abs(candidate.Height - preferredHeight) +
            (int)(Math.Abs(candidate.FrameRate - preferredFps) * 10) +
            FormatPenalty(candidate.Subtype);
        var currentScore =
            Math.Abs(current.Width - preferredWidth) +
            Math.Abs(current.Height - preferredHeight) +
            (int)(Math.Abs(current.FrameRate - preferredFps) * 10) +
            FormatPenalty(current.Subtype);
        return candidateScore < currentScore;
    }

    private static int FormatPenalty(Guid subtype)
    {
        if (subtype == VideoFormatGuids.NV12)
            return 0;

        if (subtype == VideoFormatGuids.YUY2)
            return 20;

        if (subtype == VideoFormatGuids.Rgb32 || subtype == VideoFormatGuids.Argb32)
            return 30;

        return 1000;
    }

    private static bool IsSupportedNativeSubtype(Guid subtype) =>
        subtype == VideoFormatGuids.NV12 ||
        subtype == VideoFormatGuids.YUY2 ||
        subtype == VideoFormatGuids.Rgb32 ||
        subtype == VideoFormatGuids.Argb32;

    private static Guid ReadSubtype(IMFMediaType mediaType, Guid fallback)
    {
        try
        {
            return mediaType.GetGUID(MediaTypeAttributeKeys.Subtype);
        }
        catch
        {
            return fallback;
        }
    }

    private static (int Width, int Height) ReadFrameSize(
        IMFMediaType mediaType,
        int fallbackWidth,
        int fallbackHeight)
    {
        try
        {
            var packed = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            var width = (int)(packed >> 32);
            var height = (int)(packed & 0xFFFFFFFF);
            if (width > 0 && height > 0)
                return (width, height);
        }
        catch
        {
            // Use caller-provided fallback below.
        }

        return (fallbackWidth, fallbackHeight);
    }

    private static double ReadFrameRate(IMFMediaType mediaType, double fallback)
    {
        try
        {
            var packed = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameRate);
            var numerator = (uint)(packed >> 32);
            var denominator = (uint)(packed & 0xFFFFFFFF);
            if (numerator > 0)
                return numerator / (double)Math.Max(1, denominator);
        }
        catch
        {
            // Use caller-provided fallback below.
        }

        return fallback;
    }

    private static int ReadStride(IMFMediaType mediaType, int width, Guid subtype)
    {
        try
        {
            var raw = mediaType.GetUInt32(MediaTypeAttributeKeys.DefaultStride);
            var stride = unchecked((int)raw);
            if (stride != 0)
                return stride;
        }
        catch
        {
            // Use format-specific packed stride below.
        }

        return ReadFallbackStride(width, subtype);
    }

    private static ID3D11Texture2D CreateCpuUploadTexture(ID3D11Device device, uint width, uint height)
    {
        var description = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Write
        };

        return device.CreateTexture2D(description);
    }

    private unsafe void CopySourceBufferToStagingTexture(IntPtr sourcePointer, int currentLength)
    {
        if (_device is null || _stagingTexture is null)
            throw new InvalidOperationException("Webcam capture session is not open.");

        var width = checked((int)FrameSize.Width);
        var height = checked((int)FrameSize.Height);
        var sourceStride = _sourceStride == 0 ? ReadFallbackStride(width, _sourceSubtype) : _sourceStride;
        var sourceRowPitch = Math.Abs(sourceStride);
        var requiredBytes = _sourceSubtype == VideoFormatGuids.NV12
            ? checked(sourceRowPitch * height + sourceRowPitch * ((height + 1) / 2))
            : checked(sourceRowPitch * height);
        if (currentLength < requiredBytes)
        {
            throw new InvalidOperationException(
                $"Media Foundation webcam buffer has {currentLength} bytes, expected at least {requiredBytes}.");
        }

        var mapped = _device.Context.Map(_stagingTexture, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var destination = new Span<byte>(
                mapped.DataPointer.ToPointer(),
                checked((int)mapped.RowPitch * height));
            var destinationRowPitch = checked((int)mapped.RowPitch);
            var source = (byte*)sourcePointer.ToPointer();

            if (_sourceSubtype == VideoFormatGuids.NV12)
            {
                CopyNv12ToBgra(source, sourceRowPitch, destination, destinationRowPitch, width, height);
            }
            else if (_sourceSubtype == VideoFormatGuids.YUY2)
            {
                CopyYuy2ToBgra(source, sourceStride, sourceRowPitch, destination, destinationRowPitch, width, height);
            }
            else
            {
                CopyRgb32ToBgra(source, sourceStride, sourceRowPitch, destination, destinationRowPitch, width, height);
            }
        }
        finally
        {
            _device.Context.Unmap(_stagingTexture, 0);
        }
    }

    private static int ReadFallbackStride(int width, Guid subtype)
    {
        if (subtype == VideoFormatGuids.NV12)
            return width;

        if (subtype == VideoFormatGuids.YUY2)
            return checked(width * 2);

        return checked(width * 4);
    }

    private static unsafe void CopyRgb32ToBgra(
        byte* source,
        int sourceStride,
        int sourceRowPitch,
        Span<byte> destination,
        int destinationRowPitch,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        {
            var sourceY = sourceStride < 0 ? height - 1 - y : y;
            var sourceRow = source + checked(sourceY * sourceRowPitch);
            var destinationOffset = checked(y * destinationRowPitch);

            for (var x = 0; x < width; x++)
            {
                var sourceOffset = checked(x * 4);
                var destinationPixel = destinationOffset + sourceOffset;
                destination[destinationPixel] = sourceRow[sourceOffset];
                destination[destinationPixel + 1] = sourceRow[sourceOffset + 1];
                destination[destinationPixel + 2] = sourceRow[sourceOffset + 2];
                destination[destinationPixel + 3] = byte.MaxValue;
            }
        }
    }

    private static unsafe void CopyYuy2ToBgra(
        byte* source,
        int sourceStride,
        int sourceRowPitch,
        Span<byte> destination,
        int destinationRowPitch,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        {
            var sourceY = sourceStride < 0 ? height - 1 - y : y;
            var sourceRow = source + checked(sourceY * sourceRowPitch);
            var destinationOffset = checked(y * destinationRowPitch);

            for (var x = 0; x < width; x += 2)
            {
                var packed = checked(x * 2);
                var y0 = sourceRow[packed];
                var u = sourceRow[packed + 1];
                var y1 = x + 1 < width ? sourceRow[packed + 2] : y0;
                var v = x + 1 < width ? sourceRow[packed + 3] : u;

                WriteBgra(destination, destinationOffset + checked(x * 4), y0, u, v);
                if (x + 1 < width)
                    WriteBgra(destination, destinationOffset + checked((x + 1) * 4), y1, u, v);
            }
        }
    }

    private static unsafe void CopyNv12ToBgra(
        byte* source,
        int sourceRowPitch,
        Span<byte> destination,
        int destinationRowPitch,
        int width,
        int height)
    {
        var uvPlane = source + checked(sourceRowPitch * height);

        for (var y = 0; y < height; y++)
        {
            var yRow = source + checked(y * sourceRowPitch);
            var uvRow = uvPlane + checked((y / 2) * sourceRowPitch);
            var destinationOffset = checked(y * destinationRowPitch);

            for (var x = 0; x < width; x++)
            {
                var uvOffset = checked((x / 2) * 2);
                WriteBgra(
                    destination,
                    destinationOffset + checked(x * 4),
                    yRow[x],
                    uvRow[uvOffset],
                    uvRow[uvOffset + 1]);
            }
        }
    }

    private static void WriteBgra(
        Span<byte> destination,
        int destinationPixel,
        byte y,
        byte u,
        byte v)
    {
        var c = Math.Max(0, y - 16);
        var d = u - 128;
        var e = v - 128;

        destination[destinationPixel] = ClampToByte((298 * c + 516 * d + 128) >> 8);
        destination[destinationPixel + 1] = ClampToByte((298 * c - 100 * d - 208 * e + 128) >> 8);
        destination[destinationPixel + 2] = ClampToByte((298 * c + 409 * e + 128) >> 8);
        destination[destinationPixel + 3] = byte.MaxValue;
    }

    private static byte ClampToByte(int value) =>
        (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);

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

    private static string ReadString(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetAllocatedString(key) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void DisposeSessionResources()
    {
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _sourceReader?.Dispose();
        _sourceReader = null;
        _mediaSource?.Dispose();
        _mediaSource = null;
        _device?.Dispose();
        _device = null;
        _mediaFoundationRuntimeLease?.Dispose();
        _mediaFoundationRuntimeLease = null;
    }

    private sealed record WebcamNativeMediaType(int Width, int Height, double FrameRate, Guid Subtype);
}
