using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;

namespace Weatherly.Render;

/// <summary>
/// Draws the sprites of a <see cref="RenderFrame"/> with one instanced call. Sprites outside the target
/// are culled on the CPU first, so a monitor only uploads what it shows. Allocation-free once the
/// buffers have grown to the working size.
/// </summary>
internal sealed class SpriteBatch : IDisposable
{
    private const int InitialCapacity = 1024;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11BlendState _blendState;
    private readonly ID3D11RasterizerState _rasterizerState;
    private Sprite[] _visible = new Sprite[InitialCapacity];
    private ID3D11Buffer? _buffer;
    private ID3D11ShaderResourceView? _view;
    private int _bufferCapacity;

    public SpriteBatch(ID3D11Device device, ID3D11BlendState premultipliedOver)
    {
        _device = device;
        _context = device.ImmediateContext;
        _blendState = premultipliedOver;
        _vertexShader = device.CreateVertexShader(ShaderLibrary.Load("Sprite.vs"));
        _pixelShader = device.CreatePixelShader(ShaderLibrary.Load("Sprite.ps"));
        _constants = device.CreateBuffer((uint)Marshal.SizeOf<SpriteConstants>(), BindFlags.ConstantBuffer);
        _rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    /// <summary>Number of sprites sent to the GPU by the last <see cref="Draw"/>.</summary>
    public int LastDrawnCount { get; private set; }

    /// <summary>Draws the sprites that overlap <paramref name="area"/> (desktop pixels) onto <paramref name="target"/>.</summary>
    public void Draw(ID3D11RenderTargetView target, PixelSize size, PixelRect area, ReadOnlySpan<Sprite> sprites, float opacity = 1f)
    {
        int count = Cull(area, sprites);
        LastDrawnCount = count;
        if (count == 0)
        {
            return;
        }

        EnsureBufferCapacity(count);
        MappedSubresource mapped = _context.Map(_buffer!, 0, MapMode.WriteDiscard);
        try
        {
            _visible.AsSpan(0, count).CopyTo(mapped.AsSpan<Sprite>(count));
        }
        finally
        {
            _context.Unmap(_buffer!, 0);
        }

        var constants = new SpriteConstants(new Vector4(size.Width, size.Height, area.Left, area.Top), new Vector4(Math.Clamp(opacity, 0f, 1f), 0f, 0f, 0f));
        _context.UpdateSubresource(in constants, _constants);

        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(_blendState);
        _context.RSSetViewport(0f, 0f, size.Width, size.Height);
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.VSSetShader(_vertexShader);
        _context.VSSetShaderResource(0, _view!);
        _context.VSSetConstantBuffer(0, _constants);
        _context.PSSetShader(_pixelShader);
        _context.DrawInstanced(4, (uint)count, 0, 0);
    }

    public void Dispose()
    {
        _view?.Dispose();
        _buffer?.Dispose();
        _rasterizerState.Dispose();
        _constants.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>Mirror of the cbuffer in Sprite.vs.hlsl: target size and offset, then opacity.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SpriteConstants(Vector4 TargetAndOffset, Vector4 Opacity);

    private int Cull(PixelRect area, ReadOnlySpan<Sprite> sprites)
    {
        if (_visible.Length < sprites.Length)
        {
            _visible = new Sprite[Math.Max(sprites.Length, _visible.Length * 2)];
        }

        int count = 0;
        foreach (ref readonly Sprite sprite in sprites)
        {
            float radius = sprite.BoundingRadius + 1f;
            if (sprite.Center.X + radius >= area.Left && sprite.Center.X - radius <= area.Right
                && sprite.Center.Y + radius >= area.Top && sprite.Center.Y - radius <= area.Bottom)
            {
                _visible[count++] = sprite;
            }
        }

        return count;
    }

    private void EnsureBufferCapacity(int required)
    {
        if (_bufferCapacity >= required)
        {
            return;
        }

        _view?.Dispose();
        _buffer?.Dispose();

        int capacity = Math.Max(required, Math.Max(_bufferCapacity * 2, InitialCapacity));
        _buffer = _device.CreateBuffer(new BufferDescription(
            (uint)(capacity * Sprite.SizeInBytes),
            BindFlags.ShaderResource,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.BufferStructured,
            Sprite.SizeInBytes));
        _view = _device.CreateShaderResourceView(_buffer);
        _bufferCapacity = capacity;
    }
}
