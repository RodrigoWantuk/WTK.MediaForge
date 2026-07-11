using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.DesktopDuplication;

internal interface IDesktopDuplicationSession : IDisposable
{
    D3D11GpuDevice Device { get; }

    FrameSize TextureSize { get; }

    Format TextureFormat { get; }

    CaptureSessionInfo? SessionInfo { get; }

    void Start(CaptureSourceInfo source);

    bool TryAcquireNextFrame(out ID3D11Texture2D acquiredTexture, out OutduplFrameInfo frameInfo);

    void ReleaseFrame();

    void Stop();
}

internal interface IDesktopDuplicationSessionFactory
{
    IDesktopDuplicationSession Create();
}

internal sealed class DesktopDuplicationSessionFactory : IDesktopDuplicationSessionFactory
{
    public IDesktopDuplicationSession Create() => new DesktopDuplicationSession();
}
