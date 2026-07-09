using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media;

[SupportedOSPlatform("windows")]
internal sealed class WindowsStaticImageAssetDecoder : IStaticImageAssetDecoder
{
    public StaticCpuAsset Decode(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            throw new FileNotFoundException("Image file was not found.", path);

        if (!StaticImageAssetFormats.IsSupportedExtension(path))
        {
            throw new NotSupportedException(
                $"Image format '{Path.GetExtension(path)}' is not supported. PNG and JPEG are approved for MVP.");
        }

        using var bitmap = new Bitmap(path);
        var width = bitmap.Width;
        var height = bitmap.Height;

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Image '{path}' has invalid dimensions {width}x{height}.");

        var pixels = new byte[width * height * 4];
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var stride = data.Stride;
            var source = data.Scan0;
            for (var y = 0; y < height; y++)
            {
                var sourceRow = source + (y * stride);
                var destOffset = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var sourcePixel = sourceRow + (x * 4);
                    pixels[destOffset + (x * 4) + 0] = System.Runtime.InteropServices.Marshal.ReadByte(sourcePixel + 2);
                    pixels[destOffset + (x * 4) + 1] = System.Runtime.InteropServices.Marshal.ReadByte(sourcePixel + 1);
                    pixels[destOffset + (x * 4) + 2] = System.Runtime.InteropServices.Marshal.ReadByte(sourcePixel + 0);
                    pixels[destOffset + (x * 4) + 3] = System.Runtime.InteropServices.Marshal.ReadByte(sourcePixel + 3);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new StaticCpuAsset
        {
            Path = path,
            Size = new FrameSize((uint)width, (uint)height),
            PixelFormat = RenderPixelFormat.Rgba8Unorm,
            Pixels = pixels,
            TransportKind = MediaTransportKind.StaticCpuAsset
        };
    }
}
