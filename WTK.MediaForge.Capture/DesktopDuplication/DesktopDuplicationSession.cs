using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.D3D11;
using ResultCode = Vortice.DXGI.ResultCode;

namespace WTK.MediaForge.Capture.DesktopDuplication;

internal sealed class DesktopDuplicationSession : IDisposable
{
    private D3D11GpuDevice? _device;
    private IDXGIOutputDuplication? _duplication;
    private bool _disposed;

    public D3D11GpuDevice Device =>
        _device ?? throw new InvalidOperationException("Desktop duplication session is not started.");

    public FrameSize TextureSize { get; private set; }

    public Format TextureFormat { get; private set; }

    public CaptureSessionInfo? SessionInfo { get; private set; }

    public void Start(CaptureSourceInfo source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_duplication is not null)
            return;

        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        if (factory.EnumAdapters1(source.AdapterIndex, out IDXGIAdapter1? adapter).Failure || adapter is null)
            throw new InvalidOperationException($"Adapter not found: {source.AdapterIndex}");

        _device = D3D11GpuDevice.CreateForAdapter(adapter);

        GpuAdapterLuid captureAdapterLuid = new()
        {
            LowPart = adapter.Description1.Luid.LowPart,
            HighPart = adapter.Description1.Luid.HighPart
        };

        if (adapter.EnumOutputs(source.OutputIndex, out IDXGIOutput? output).Failure || output is null)
            throw new InvalidOperationException($"Output not found: {source.OutputIndex}");

        using (output)
        using (IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>())
        {
            _duplication = output1.DuplicateOutput(_device.Device);
        }

        var duplicationDescription = _duplication.Description;

        TextureSize = new FrameSize(
            duplicationDescription.ModeDescription.Width,
            duplicationDescription.ModeDescription.Height);
        TextureFormat = duplicationDescription.ModeDescription.Format;

        SessionInfo = new CaptureSessionInfo
        {
            CaptureAdapterLuid = captureAdapterLuid,
            DuplicationTextureSize = TextureSize,
            TextureFormat = TextureFormat.ToString(),
            RefreshRateNumerator = duplicationDescription.ModeDescription.RefreshRate.Numerator,
            RefreshRateDenominator = duplicationDescription.ModeDescription.RefreshRate.Denominator
        };

        WarmUpDuplication();
    }

    public bool TryAcquireNextFrame(out ID3D11Texture2D acquiredTexture, out OutduplFrameInfo frameInfo)
    {
        acquiredTexture = null!;
        frameInfo = default;

        if (_duplication is null)
            return false;

        var result = _duplication.AcquireNextFrame(
            0,
            out frameInfo,
            out IDXGIResource? desktopResource);

        if (result.Code == ResultCode.WaitTimeout.Code)
            return false;

        if (result.Failure)
            throw new InvalidOperationException($"AcquireNextFrame failed: {result}");

        acquiredTexture = desktopResource!.QueryInterface<ID3D11Texture2D>();
        desktopResource.Dispose();

        var description = acquiredTexture.Description;

        if (description.Width != TextureSize.Width ||
            description.Height != TextureSize.Height ||
            description.Format != TextureFormat)
        {
            TextureSize = new FrameSize(description.Width, description.Height);
            TextureFormat = description.Format;

            if (SessionInfo is not null)
            {
                SessionInfo = new CaptureSessionInfo
                {
                    CaptureAdapterLuid = SessionInfo.CaptureAdapterLuid,
                    DuplicationTextureSize = TextureSize,
                    TextureFormat = TextureFormat.ToString(),
                    RefreshRateNumerator = SessionInfo.RefreshRateNumerator,
                    RefreshRateDenominator = SessionInfo.RefreshRateDenominator
                };
            }
        }

        return true;
    }

    public void ReleaseFrame()
    {
        _duplication?.ReleaseFrame();
    }

    public void Stop()
    {
        _duplication?.Dispose();
        _duplication = null;

        _device?.Dispose();
        _device = null;

        TextureSize = default;
        TextureFormat = default;
        SessionInfo = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    private void WarmUpDuplication()
    {
        if (_duplication is null)
            return;

        for (var i = 0; i < 10; i++)
        {
            var result = _duplication.AcquireNextFrame(
                16,
                out _,
                out IDXGIResource? desktopResource);

            if (result.Code == ResultCode.WaitTimeout.Code)
                continue;

            if (result.Failure)
                break;

            desktopResource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }
}
