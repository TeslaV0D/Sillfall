using Sillfall.Core.Geometry;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Sillfall.Render;

/// <summary>
/// A render target without a window, in the same pixel format as the overlay swap chains.
/// Used to render frames headless (tests, golden images) and read them back.
/// </summary>
public sealed class OffscreenTarget : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11Texture2D _texture;
    private readonly ID3D11Texture2D _staging;

    public OffscreenTarget(ID3D11Device device, PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Height, 1);

        _device = device;
        Size = size;
        _texture = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)size.Width,
            (uint)size.Height,
            arraySize: 1,
            mipLevels: 1,
            BindFlags.RenderTarget));
        View = device.CreateRenderTargetView(_texture);
        _staging = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)size.Width,
            (uint)size.Height,
            arraySize: 1,
            mipLevels: 1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read));
    }

    public PixelSize Size { get; }

    public ID3D11RenderTargetView View { get; }

    /// <summary>The texture behind <see cref="View"/>, for example to draw into it with Direct2D.</summary>
    public ID3D11Texture2D Texture => _texture;

    /// <summary>
    /// Copies the rendered image to memory: 4 bytes per pixel in the order blue, green, red, alpha
    /// (premultiplied), rows from top to bottom without padding.
    /// </summary>
    public byte[] ReadPixels()
    {
        ID3D11DeviceContext context = _device.ImmediateContext;
        context.CopyResource(_staging, _texture);

        int rowLength = Size.Width * 4;
        byte[] pixels = new byte[rowLength * Size.Height];

        MappedSubresource mapped = context.Map(_staging, 0, MapMode.Read);
        try
        {
            // Rows in GPU memory can be longer than the image (RowPitch), so copy row by row.
            ReadOnlySpan<byte> source = mapped.AsSpan((int)mapped.RowPitch * Size.Height);
            for (int y = 0; y < Size.Height; y++)
            {
                source.Slice(y * (int)mapped.RowPitch, rowLength).CopyTo(pixels.AsSpan(y * rowLength, rowLength));
            }
        }
        finally
        {
            context.Unmap(_staging, 0);
        }

        return pixels;
    }

    public void Dispose()
    {
        View.Dispose();
        _staging.Dispose();
        _texture.Dispose();
    }
}
