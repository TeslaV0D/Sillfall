using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Weatherly.Render;

/// <summary>
/// Direct2D on the same device as the Direct3D pipelines (DXGI surface interop, PLAN.md section 6).
/// Vector shapes and text stay sharp at every DPI; used for visitors, share cards and debug overlays.
/// Draws into any BGRA texture of the device: swap chain back buffers and offscreen targets alike.
/// </summary>
public sealed class Direct2DCanvas : IDisposable
{
    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _device;
    private ID2D1Bitmap1? _target;

    public Direct2DCanvas(ID3D11Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        _device = _factory.CreateDevice(dxgiDevice);
        Context = _device.CreateDeviceContext(DeviceContextOptions.None);
    }

    /// <summary>The Direct2D context. Valid for drawing between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public ID2D1DeviceContext Context { get; }

    /// <summary>
    /// Starts drawing into <paramref name="texture"/>. Its pixels are premultiplied BGRA, like everything
    /// the overlay renders, and Direct2D blends "over" what Direct3D drew before.
    /// </summary>
    public ID2D1DeviceContext Begin(ID3D11Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_target is not null)
        {
            throw new InvalidOperationException("Begin was called twice without End.");
        }

        using IDXGISurface surface = texture.QueryInterface<IDXGISurface>();
        var properties = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _target = Context.CreateBitmapFromDxgiSurface(surface, properties);
        Context.Target = _target;
        Context.BeginDraw();
        return Context;
    }

    /// <summary>Finishes drawing and releases the target.</summary>
    public void End()
    {
        if (_target is null)
        {
            throw new InvalidOperationException("End was called without Begin.");
        }

        try
        {
            Context.EndDraw().CheckError();
        }
        finally
        {
            Context.Target = null;
            _target.Dispose();
            _target = null;
        }
    }

    public void Dispose()
    {
        _target?.Dispose();
        Context.Dispose();
        _device.Dispose();
        _factory.Dispose();
    }
}
