using Vortice.Direct3D11;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Graphics.D3D11;

public sealed class D3D11TextureFrame
{
    public D3D11TextureFrame(
        ID3D11Texture2D texture,
        nint sharedHandle,
        FrameSize size,
        long frameNumber,
        long timestamp)
    {
        Texture = texture;
        SharedHandle = sharedHandle;
        Size = size;
        FrameNumber = frameNumber;
        Timestamp = timestamp;
    }

    public ID3D11Texture2D Texture { get; }

    public nint SharedHandle { get; }

    public bool HasSharedHandle => SharedHandle != 0;

    public FrameSize Size { get; }

    public long FrameNumber { get; }

    public long Timestamp { get; }
}