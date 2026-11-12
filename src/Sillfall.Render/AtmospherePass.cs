using System.Numerics;
using System.Runtime.InteropServices;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sillfall.Render;

/// <summary>
/// Fog and sunlight (PLAN.md, sections 4.4 and 4.5) as full-screen passes at a quarter of the resolution:
/// <list type="number">
/// <item>a mask of the windows (white sky, black windows),</item>
/// <item>fog noise and god rays that march through the mask towards the sun,</item>
/// <item>bilinear upscaling over the target, premultiplied.</item>
/// </list>
/// The quarter-size textures are kept per monitor size, so several monitors do not recreate them every frame.
/// </summary>
internal sealed class AtmospherePass : IDisposable
{
    /// <summary>Atmosphere resolution relative to the target.</summary>
    public const int Downscale = 4;

    private const int MaxCachedSizes = 4;

    private static readonly Color4 s_openSky = new(1f, 1f, 1f, 1f);
    private static readonly Color4 s_transparent = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 s_window = new(0f, 0f, 0f, 1f);

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _fullscreenTriangle;
    private readonly ID3D11PixelShader _atmosphere;
    private readonly ID3D11PixelShader _upsample;
    private readonly ID3D11Buffer _atmosphereConstants;
    private readonly ID3D11Buffer _upsampleConstants;
    private readonly ID3D11SamplerState _linear;
    private readonly ID3D11BlendState _premultipliedOver;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly RectBatch _rects;
    private readonly List<Targets> _cache = new(MaxCachedSizes);

    public AtmospherePass(ID3D11Device device, ID3D11VertexShader fullscreenTriangle, ID3D11BlendState premultipliedOver, ID3D11RasterizerState rasterizerState)
    {
        _device = device;
        _context = device.ImmediateContext;
        _fullscreenTriangle = fullscreenTriangle;
        _premultipliedOver = premultipliedOver;
        _rasterizerState = rasterizerState;
        _atmosphere = device.CreatePixelShader(ShaderLibrary.Load("Atmosphere.ps"));
        _upsample = device.CreatePixelShader(ShaderLibrary.Load("Upsample.ps"));
        _atmosphereConstants = device.CreateBuffer((uint)Marshal.SizeOf<AtmosphereConstants>(), BindFlags.ConstantBuffer);
        _upsampleConstants = device.CreateBuffer((uint)Marshal.SizeOf<Vector4>(), BindFlags.ConstantBuffer);
        _linear = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _rects = new RectBatch(device);
    }

    /// <summary>Draws the fog and the sunlight of <paramref name="frame"/> over <paramref name="target"/>.</summary>
    /// <param name="target">Full-size render target.</param>
    /// <param name="size">Size of <paramref name="target"/> in pixels.</param>
    /// <param name="area">The desktop area the target shows.</param>
    /// <param name="frame">The frame with the fog, the sun and the window rectangles.</param>
    /// <param name="opacity">How strongly the weather shows on this monitor, 0 to 1.</param>
    public void Draw(ID3D11RenderTargetView target, PixelSize size, PixelRect area, RenderFrame frame, float opacity = 1f)
    {
        var quarter = new PixelSize(Math.Max(1, (size.Width + Downscale - 1) / Downscale), Math.Max(1, (size.Height + Downscale - 1) / Downscale));
        Targets targets = TargetsFor(quarter);

        // 1. The windows as a mask.
        _context.ClearRenderTargetView(targets.MaskView, s_openSky);
        _rects.Clear();
        foreach (PixelRect window in frame.Occluders)
        {
            var local = new PixelRect(
                (int)MathF.Floor((window.Left - area.Left) / (float)Downscale),
                (int)MathF.Floor((window.Top - area.Top) / (float)Downscale),
                (int)MathF.Ceiling((window.Right - area.Left) / (float)Downscale),
                (int)MathF.Ceiling((window.Bottom - area.Top) / (float)Downscale));
            if (local.Right > 0 && local.Bottom > 0 && local.Left < quarter.Width && local.Top < quarter.Height)
            {
                _rects.Add(local, s_window);
            }
        }

        if (_rects.Count > 0)
        {
            _rects.Draw(targets.MaskView, quarter);
        }

        // 2. Fog and rays at a quarter of the resolution.
        var constants = new AtmosphereConstants(quarter, area, frame.Fog, frame.Sun);
        _context.UpdateSubresource(in constants, _atmosphereConstants);
        _context.ClearRenderTargetView(targets.LayerView, s_transparent);
        _context.OMSetRenderTargets(targets.LayerView);
        _context.OMSetBlendState(null);
        _context.RSSetViewport(0f, 0f, quarter.Width, quarter.Height);
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_fullscreenTriangle);
        _context.PSSetShader(_atmosphere);
        _context.PSSetConstantBuffer(0, _atmosphereConstants);
        _context.PSSetShaderResource(0, targets.MaskResource);
        _context.PSSetSampler(0, _linear);
        _context.Draw(3, 0);

        // 3. Up to full size, over whatever is already in the target.
        var upsample = new Vector4(size.Width, size.Height, Math.Clamp(opacity, 0f, 1f), 0f);
        _context.UpdateSubresource(in upsample, _upsampleConstants);
        _context.PSSetShaderResource(0, null!);
        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(_premultipliedOver);
        _context.RSSetViewport(0f, 0f, size.Width, size.Height);
        _context.PSSetShader(_upsample);
        _context.PSSetConstantBuffer(0, _upsampleConstants);
        _context.PSSetShaderResource(0, targets.LayerResource);
        _context.PSSetSampler(0, _linear);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
    }

    public void Dispose()
    {
        foreach (Targets targets in _cache)
        {
            targets.Dispose();
        }

        _cache.Clear();
        _rects.Dispose();
        _linear.Dispose();
        _upsampleConstants.Dispose();
        _atmosphereConstants.Dispose();
        _upsample.Dispose();
        _atmosphere.Dispose();
    }

    private Targets TargetsFor(PixelSize size)
    {
        foreach (Targets cached in _cache)
        {
            if (cached.Size == size)
            {
                return cached;
            }
        }

        if (_cache.Count == MaxCachedSizes)
        {
            _cache[0].Dispose();
            _cache.RemoveAt(0);
        }

        var targets = new Targets(_device, size);
        _cache.Add(targets);
        return targets;
    }

    /// <summary>The two quarter-size textures for one monitor size.</summary>
    private sealed class Targets : IDisposable
    {
        private readonly ID3D11Texture2D _mask;
        private readonly ID3D11Texture2D _layer;

        public Targets(ID3D11Device device, PixelSize size)
        {
            Size = size;
            _mask = device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, (uint)size.Width, (uint)size.Height, arraySize: 1, mipLevels: 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
            MaskView = device.CreateRenderTargetView(_mask);
            MaskResource = device.CreateShaderResourceView(_mask);

            // Half floats: the faint fog would band in 8 bits.
            _layer = device.CreateTexture2D(new Texture2DDescription(
                Format.R16G16B16A16_Float, (uint)size.Width, (uint)size.Height, arraySize: 1, mipLevels: 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
            LayerView = device.CreateRenderTargetView(_layer);
            LayerResource = device.CreateShaderResourceView(_layer);
        }

        public PixelSize Size { get; }

        public ID3D11RenderTargetView MaskView { get; }

        public ID3D11ShaderResourceView MaskResource { get; }

        public ID3D11RenderTargetView LayerView { get; }

        public ID3D11ShaderResourceView LayerResource { get; }

        public void Dispose()
        {
            LayerResource.Dispose();
            LayerView.Dispose();
            _layer.Dispose();
            MaskResource.Dispose();
            MaskView.Dispose();
            _mask.Dispose();
        }
    }

    /// <summary>Mirror of the cbuffer in Atmosphere.ps.hlsl. Field order and types must match (96 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct AtmosphereConstants
    {
        private readonly Vector2 _quarterSize;
        private readonly Vector2 _monitorOrigin;
        private readonly Vector2 _monitorSize;
        private readonly float _fogDensity;
        private readonly float _fogMaxAlpha;
        private readonly Vector4 _fogColor;
        private readonly Vector2 _fogScroll;
        private readonly float _fogPhase;
        private readonly float _sunStrength;
        private readonly Vector2 _sunPosition;
        private readonly float _sunScale;
        private readonly float _padding;
        private readonly Vector4 _sunColor;

        public AtmosphereConstants(PixelSize quarter, PixelRect area, FogLayer fog, SunLayer sun)
        {
            _quarterSize = new Vector2(quarter.Width, quarter.Height);
            _monitorOrigin = new Vector2(area.Left, area.Top);
            _monitorSize = new Vector2(area.Width, area.Height);
            _fogDensity = fog.IsVisible ? fog.Density : 0f;
            _fogMaxAlpha = fog.MaxAlpha;
            _fogColor = new Vector4(fog.Color, MathF.Max(fog.Scale, 0.5f));
            _fogScroll = fog.Scroll;
            _fogPhase = fog.Phase;
            _sunStrength = sun.IsVisible ? sun.Strength : 0f;
            _sunPosition = sun.Position;
            _sunScale = MathF.Max(sun.Scale, 0.5f);
            _padding = 0f;
            _sunColor = new Vector4(sun.Color, 1f);
        }
    }
}
