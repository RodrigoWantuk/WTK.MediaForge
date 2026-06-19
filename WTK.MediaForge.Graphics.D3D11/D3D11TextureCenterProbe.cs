using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WTK.MediaForge.Graphics.D3D11;

public sealed class D3D11TextureCenterProbe : IDisposable
{
    private ID3D11Texture2D? _stagingTexture;
    private uint _stagingWidth;
    private uint _stagingHeight;
    private Format _stagingFormat;
    private bool _disposed;

    public bool TryReadCenterPixel(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11Texture2D source,
        out byte blue,
        out byte green,
        out byte red,
        out byte alpha)
    {
        blue = green = red = alpha = 0;

        var description = source.Description;
        EnsureStagingTexture(device, description.Width, description.Height, description.Format);

        if (_stagingTexture is null)
            return false;

        context.CopyResource(_stagingTexture, source);

        var mapped = context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            unsafe
            {
                int centerX = Math.Max(0, (int)(description.Width / 2));
                int centerY = Math.Max(0, (int)(description.Height / 2));
                int offset = centerY * (int)mapped.RowPitch + centerX * 4;

                byte* basePointer = (byte*)mapped.DataPointer;
                blue = basePointer[offset + 0];
                green = basePointer[offset + 1];
                red = basePointer[offset + 2];
                alpha = basePointer[offset + 3];
            }

            return true;
        }
        finally
        {
            context.Unmap(_stagingTexture, 0);
        }
    }

    private void EnsureStagingTexture(ID3D11Device device, uint width, uint height, Format format)
    {
        if (_stagingTexture is not null &&
            _stagingWidth == width &&
            _stagingHeight == height &&
            _stagingFormat == format)
        {
            return;
        }

        _stagingTexture?.Dispose();
        _stagingTexture = null;

        var description = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };

        _stagingTexture = device.CreateTexture2D(description);
        _stagingWidth = width;
        _stagingHeight = height;
        _stagingFormat = format;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
    }
}
