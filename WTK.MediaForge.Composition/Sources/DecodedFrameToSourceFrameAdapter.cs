using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Time;

namespace WTK.MediaForge.Composition.Sources;

internal interface IDecodedFrameToSourceFrameAdapter
{
    GpuFrameLease CreateSourceFrameLease(
        DecodedGpuFrame decodedFrame,
        SourceId sourceId,
        long frameNumber);
}

internal sealed class DecodedFrameToSourceFrameAdapter : IDecodedFrameToSourceFrameAdapter
{
    public static DecodedFrameToSourceFrameAdapter Instance { get; } = new();

    private DecodedFrameToSourceFrameAdapter()
    {
    }

    public GpuFrameLease CreateSourceFrameLease(
        DecodedGpuFrame decodedFrame,
        SourceId sourceId,
        long frameNumber)
    {
        ArgumentNullException.ThrowIfNull(decodedFrame);
        if (sourceId.IsEmpty)
            throw new ArgumentException("Source id cannot be empty.", nameof(sourceId));

        var textureLease = decodedFrame.TakeTextureLease();
        try
        {
            var physical = textureLease.Texture.Physical;
            if (physical is not IGpuFrameHandleProvider frameHandleProvider)
            {
                throw new NotSupportedException(
                    "Decoded GPU frame texture does not expose a renderable GPU frame handle.");
            }

            var handle = frameHandleProvider.FrameHandle;
            var size = new FrameSize(
                checked((uint)textureLease.Width),
                checked((uint)textureLease.Height));

            var reference = new GpuFrameReference
            {
                SourceId = sourceId,
                Backend = handle.Backend,
                Handle = handle,
                TextureSize = size,
                LogicalSize = size,
                FrameNumber = frameNumber,
                Timestamp = ToMediaTime(decodedFrame.PresentationTime)
            };

            return GpuFrameLease.Create(reference, textureLease.Dispose);
        }
        catch
        {
            textureLease.Dispose();
            throw;
        }
    }

    private static MediaTime ToMediaTime(TimeSpan presentationTime) =>
        new(checked(presentationTime.Ticks * 100L));
}
