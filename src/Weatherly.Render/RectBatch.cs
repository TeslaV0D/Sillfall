using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Weatherly.Core.Geometry;

namespace Weatherly.Render;

/// <summary>
/// Collects colored rectangles and draws them in one instanced call. Colors are premultiplied and
/// blended "over" the target. Used by the debug layers, and the base for particle rendering.
/// </summary>
internal sealed class RectBatch : IDisposable
{
    private const int InitialCapacity = 256;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11BlendState _blendState;
    private readonly ID3D11RasterizerState _rasterizerState;
    private RectInstance[] _instances = new RectInstance[InitialCapacity];
    private ID3D11Buffer? _instanceBuffer;
    private ID3D11ShaderResourceView? _instanceView;
    private int _bufferCapacity;
    private int _count;

    public RectBatch(ID3D11Device device)
    {
        _device = device;
        _context = device.ImmediateContext;
        _vertexShader = device.CreateVertexShader(ShaderLibrary.Load("Rect.vs"));
        _pixelShader = device.CreatePixelShader(ShaderLibrary.Load("Color.ps"));
        _constants = device.CreateBuffer((uint)Marshal.SizeOf<Vector4>(), BindFlags.ConstantBuffer);
        _blendState = device.CreateBlendState(PremultipliedOver());
        _rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public int Count => _count;

    public void Clear() => _count = 0;

    /// <summary>Adds a rectangle in target pixels with a premultiplied color.</summary>
    public void Add(PixelRect rect, Vector4 premultipliedColor)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        if (_count == _instances.Length)
        {
            Array.Resize(ref _instances, _instances.Length * 2);
        }

        _instances[_count++] = new RectInstance(new Vector4(rect.Left, rect.Top, rect.Right, rect.Bottom), premultipliedColor);
    }

    /// <summary>Draws all collected rectangles onto <paramref name="target"/>.</summary>
    public void Draw(ID3D11RenderTargetView target, PixelSize size)
    {
        if (_count == 0)
        {
            return;
        }

        EnsureBufferCapacity(_count);

        MappedSubresource mapped = _context.Map(_instanceBuffer!, 0, MapMode.WriteDiscard);
        try
        {
            _instances.AsSpan(0, _count).CopyTo(mapped.AsSpan<RectInstance>(_count));
        }
        finally
        {
            _context.Unmap(_instanceBuffer!, 0);
        }

        var constants = new Vector4(size.Width, size.Height, 0f, 0f);
        _context.UpdateSubresource(in constants, _constants);

        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(_blendState);
        _context.RSSetViewport(0f, 0f, size.Width, size.Height);
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.VSSetShader(_vertexShader);
        _context.VSSetShaderResource(0, _instanceView!);
        _context.VSSetConstantBuffer(0, _constants);
        _context.PSSetShader(_pixelShader);
        _context.DrawInstanced(4, (uint)_count, 0, 0);
    }

    public void Dispose()
    {
        _instanceView?.Dispose();
        _instanceBuffer?.Dispose();
        _rasterizerState.Dispose();
        _blendState.Dispose();
        _constants.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>"Source over destination" for premultiplied colors.</summary>
    public static BlendDescription PremultipliedOver()
    {
        var description = new BlendDescription();
        description.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        return description;
    }

    private void EnsureBufferCapacity(int required)
    {
        if (_bufferCapacity >= required)
        {
            return;
        }

        _instanceView?.Dispose();
        _instanceBuffer?.Dispose();

        int capacity = Math.Max(required, Math.Max(_bufferCapacity * 2, InitialCapacity));
        int stride = Marshal.SizeOf<RectInstance>();
        _instanceBuffer = _device.CreateBuffer(new BufferDescription(
            (uint)(capacity * stride),
            BindFlags.ShaderResource,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.BufferStructured,
            (uint)stride));
        _instanceView = _device.CreateShaderResourceView(_instanceBuffer);
        _bufferCapacity = capacity;
    }

    /// <summary>Mirror of RectInstance in Rect.vs.hlsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RectInstance(Vector4 Rect, Vector4 Color);
}
