using Vortice.Direct3D11;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Render;

/// <summary>
/// Renders overlay frames without a window, with the same painter as the overlays. With WARP the
/// output is deterministic, which makes it suitable for previews and golden images.
/// </summary>
public sealed class HeadlessRenderer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly OverlayPainter _painter;
    private OffscreenTarget? _target;

    public HeadlessRenderer(GraphicsDriver driver = GraphicsDriver.Warp)
    {
        _device = GraphicsDeviceFactory.Create(driver);
        _painter = new OverlayPainter(_device);
    }

    /// <summary>Renders <paramref name="frame"/> at <paramref name="size"/> and returns premultiplied BGRA pixels.</summary>
    public byte[] Render(PixelSize size, in OverlayFrame frame)
    {
        if (_target is null || _target.Size != size)
        {
            _target?.Dispose();
            _target = new OffscreenTarget(_device, size);
        }

        _painter.Paint(_target.View, size, frame);
        return _target.ReadPixels();
    }

    public void Dispose()
    {
        _target?.Dispose();
        _painter.Dispose();
        _device.Dispose();
    }
}
