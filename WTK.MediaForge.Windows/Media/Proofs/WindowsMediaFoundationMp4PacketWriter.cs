using System.Runtime.InteropServices;
using Vortice.MediaFoundation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal static class WindowsMediaFoundationMp4PacketWriter
{
    public static void Write(
        string outputPath,
        IReadOnlyList<EncodedVideoPacket> packets,
        FrameSize size,
        int framesPerSecond)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(packets);
        if (packets.Count == 0)
            throw new InvalidOperationException("Cannot write a Media Foundation MP4 proof asset without packets.");

        using var runtime = MediaFoundationRuntime.Acquire();
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(SinkWriterAttributeKeys.DisableThrottling, true).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, true).CheckError();

        using var writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, attributes);
        using var outputType = CreateH264MediaType(size, framesPerSecond, packets);
        var streamIndex = writer.AddStream(outputType);
        using var inputType = CreateH264MediaType(size, framesPerSecond, packets);
        writer.SetInputMediaType(streamIndex, inputType, null);
        writer.BeginWriting();

        foreach (var packet in packets)
        {
            using var sample = CreateSample(packet);
            writer.WriteSample(streamIndex, sample);
        }

        writer.Finalize();
    }

    private static IMFMediaType CreateH264MediaType(
        FrameSize size,
        int framesPerSecond,
        IReadOnlyList<EncodedVideoPacket> packets)
    {
        var mediaType = MediaFactory.MFCreateMediaType();
        mediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        mediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
        MediaFactory.MFSetAttributeSize(mediaType, MediaTypeAttributeKeys.FrameSize, size.Width, size.Height).CheckError();
        MediaFactory.MFSetAttributeRatio(mediaType, MediaTypeAttributeKeys.FrameRate, (uint)Math.Max(1, framesPerSecond), 1).CheckError();
        MediaFactory.MFSetAttributeRatio(mediaType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        mediaType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();

        var sequenceHeader = packets
            .Select(static packet => packet.CodecConfiguration)
            .FirstOrDefault(static configuration => !configuration.IsEmpty);
        if (!sequenceHeader.IsEmpty)
            mediaType.SetBlob(MediaTypeAttributeKeys.MpegSequenceHeader, sequenceHeader.ToArray()).CheckError();

        return mediaType;
    }

    private static IMFSample CreateSample(EncodedVideoPacket packet)
    {
        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException("Media Foundation MP4 proof writer accepts H.264 packets only.");

        var bytes = packet.BitstreamFormat == EncodedVideoBitstreamFormat.Avcc
            ? ConvertAvccToAnnexB(packet.Data.Span)
            : packet.Data.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException("Cannot write an empty encoded sample.");

        var sample = MediaFactory.MFCreateSample();
        var buffer = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
        nint pointer = 0;
        var maxLength = 0;
        var currentLength = 0;
        buffer.Lock(out pointer, out maxLength, out currentLength);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = bytes.Length;
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = packet.PresentationTime.Ticks;
        sample.SampleDuration = packet.Duration > TimeSpan.Zero
            ? packet.Duration.Ticks
            : TimeSpan.FromMilliseconds(16).Ticks;
        if (packet.IsKeyFrame)
            sample.Set(SampleAttributeKeys.CleanPoint, 1).CheckError();

        return sample;
    }

    private static byte[] ConvertAvccToAnnexB(ReadOnlySpan<byte> avcc)
    {
        using var output = new MemoryStream();
        var offset = 0;
        while (offset < avcc.Length)
        {
            if (offset + 4 > avcc.Length)
                throw new InvalidOperationException("AVCC H.264 packet has an incomplete NAL length prefix.");

            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(avcc.Slice(offset, 4));
            offset += 4;
            if (length == 0 || length > int.MaxValue || offset + (int)length > avcc.Length)
                throw new InvalidOperationException("AVCC H.264 packet has an invalid NAL length prefix.");

            output.Write([0x00, 0x00, 0x00, 0x01]);
            output.Write(avcc.Slice(offset, (int)length));
            offset += (int)length;
        }

        return output.ToArray();
    }
}
