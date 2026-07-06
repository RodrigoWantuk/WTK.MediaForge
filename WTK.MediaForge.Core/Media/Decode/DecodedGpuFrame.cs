using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Core.Media.Decode;

public sealed class DecodedGpuFrame : IDisposable
{
    private GpuTextureLease? _textureLease;
    private int _disposed;

    public DecodedGpuFrame(GpuTextureLease textureLease, TimeSpan presentationTime, TimeSpan duration)
    {
        _textureLease = textureLease ?? throw new ArgumentNullException(nameof(textureLease));
        PresentationTime = presentationTime;
        Duration = duration;
    }

    public GpuTextureLease TextureLease =>
        _textureLease ?? throw new ObjectDisposedException(nameof(DecodedGpuFrame));

    public TimeSpan PresentationTime { get; }

    public TimeSpan Duration { get; }

    public int Width => TextureLease.Width;

    public int Height => TextureLease.Height;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _textureLease?.Dispose();
        _textureLease = null;
    }
}

public sealed class HardwareDecodeSession
{
    public required EncodedVideoCodec Codec { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public bool PreferHardware { get; init; } = true;
}

public sealed class HardwareDecodeOpenContext
{
    public required string SourcePath { get; init; }

    public required HardwareDecodeSession Session { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
