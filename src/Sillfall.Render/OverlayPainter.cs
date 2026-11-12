using System.Numerics;
using System.Runtime.InteropServices;
using Sillfall.Core.Geometry;
using Sillfall.Core.Overlay;
using Sillfall.Core.Surfaces;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Sillfall.Render;

/// <summary>
/// Draws overlay content into a render target. Used for the on-screen overlays and for offscreen
/// targets (tests, golden images), so both go through the same pipeline.
/// Output is premultiplied alpha, and everything that is not drawn stays fully transparent.
/// </summary>
public sealed class OverlayPainter : IDisposable
{
    private static readonly Color4 s_transparent = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// Debug colors per surface edge, opaque and given in exact bytes so the rendered pixels are predictable.
    /// Edges are told apart by position as well, never by color alone.
    /// </summary>
    private static readonly Vector4 s_topColor = Opaque(51, 242, 77);
    private static readonly Vector4 s_sideColor = Opaque(255, 153, 26);
    private static readonly Vector4 s_floorColor = Opaque(51, 153, 255);

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _fullscreenTriangle;
    private readonly ID3D11PixelShader _testPattern;
    private readonly ID3D11Buffer _testPatternConstants;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly RectBatch _rects;
    private readonly ID3D11BlendState _premultipliedOver;
    private readonly SpriteBatch _sprites;
    private readonly AtmospherePass _atmosphere;
    private readonly ID3D11BlendState _replace;
    private readonly ID3D11Device _device;
    private Direct2DCanvas? _canvas;
    private VisitorPainter? _visitors;

    public OverlayPainter(ID3D11Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _context = device.ImmediateContext;
        _fullscreenTriangle = device.CreateVertexShader(ShaderLibrary.Load("FullscreenTriangle.vs"));
        _testPattern = device.CreatePixelShader(ShaderLibrary.Load("TestPattern.ps"));
        _testPatternConstants = device.CreateBuffer((uint)Marshal.SizeOf<TestPatternConstants>(), BindFlags.ConstantBuffer);
        _rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _rects = new RectBatch(device);
        _premultipliedOver = device.CreateBlendState(RectBatch.PremultipliedOver());
        _sprites = new SpriteBatch(device, _premultipliedOver);
        _atmosphere = new AtmospherePass(device, _fullscreenTriangle, _premultipliedOver, _rasterizerState);
        _replace = device.CreateBlendState(BlendDescription.Opaque);
    }

    /// <summary>Number of sprites drawn by the last <see cref="Paint"/>.</summary>
    public int LastSpriteCount => _sprites.LastDrawnCount;

    /// <summary>Thickness of the debug lines in pixels for a monitor with the given DPI scale.</summary>
    public static int DebugLineThickness(float scaleFactor) => 2 * TestPatternLayout.Create(new PixelSize(1, 1), scaleFactor).Unit;

    /// <summary>Clears <paramref name="target"/> to transparent and draws the content of <paramref name="frame"/>.</summary>
    public void Paint(ID3D11RenderTargetView target, PixelSize size, in OverlayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(target);

        _context.ClearRenderTargetView(target, s_transparent);

        if (frame.Content == OverlayContent.TestPattern)
        {
            DrawTestPattern(target, size, frame.ScaleFactor);
        }

        if (frame.Scene is { } scene)
        {
            // Sun and fog lie behind everything else (PLAN.md, section 4.7).
            if (scene.HasAtmosphere)
            {
                _atmosphere.Draw(target, size, MonitorArea(frame, size), scene, frame.Opacity);
            }

            _sprites.Draw(target, size, MonitorArea(frame, size), scene.Sprites, frame.Opacity);

            // Visitors walk on top of the weather (PLAN.md, section 4.7), drawn with Direct2D on the same target.
            if (scene.Visitors.Length > 0 || scene.Box.IsVisible)
            {
                DrawVisitors(target, MonitorArea(frame, size), scene.Visitors, scene.Box, frame.Opacity);
            }

            // App rules: windows of some processes stay clear. Their rectangles are cut back to transparent.
            if (scene.Cutouts.Length > 0)
            {
                CutOut(target, size, MonitorArea(frame, size), scene.Cutouts);
            }
        }

        // The debug layer goes on top of everything, so it stays readable while effects run.
        if (frame.Surfaces is { } surfaces)
        {
            DrawSurfaces(target, size, frame, surfaces);
        }

        // Leaves no reference to the target in the pipeline, flip-model swap chains require that before Present.
        _context.ClearState();
    }

    public void Dispose()
    {
        _visitors?.Dispose();
        _canvas?.Dispose();
        _replace.Dispose();
        _atmosphere.Dispose();
        _sprites.Dispose();
        _premultipliedOver.Dispose();
        _rects.Dispose();
        _rasterizerState.Dispose();
        _testPatternConstants.Dispose();
        _testPattern.Dispose();
        _fullscreenTriangle.Dispose();

        // The immediate context belongs to the device and is released with it.
    }

    /// <summary>Direct2D comes up on the first visitor: most frames never need it.</summary>
    private void DrawVisitors(ID3D11RenderTargetView target, PixelRect area, ReadOnlySpan<Core.Rendering.VisitorPose> visitors, Core.Rendering.VisitorBox box, float opacity)
    {
        _context.ClearState();
        _canvas ??= new Direct2DCanvas(_device);
        _visitors ??= new VisitorPainter(_canvas.Context);
        using ID3D11Resource resource = target.Resource;
        using ID3D11Texture2D texture = resource.QueryInterface<ID3D11Texture2D>();
        _canvas.Begin(texture);
        try
        {
            _visitors.Draw(visitors, area, opacity, box);
        }
        finally
        {
            _canvas.End();
        }
    }

    /// <summary>
    /// Debug layer of milestone M2: every visible surface as a band right outside its window.
    /// Top edges green, sides orange, floors blue.
    /// </summary>
    private void DrawSurfaces(ID3D11RenderTargetView target, PixelSize size, in OverlayFrame frame, SurfaceSet surfaces)
    {
        int thickness = DebugLineThickness(frame.ScaleFactor);
        _rects.Clear();
        foreach (SurfaceSegment segment in surfaces.Segments)
        {
            PixelRect band = SurfaceGeometry.ToMonitor(SurfaceGeometry.Band(segment, thickness), frame.MonitorBounds);
            Vector4 color = segment.Key.Edge switch
            {
                SurfaceEdge.Top => s_topColor,
                SurfaceEdge.Floor => s_floorColor,
                _ => s_sideColor,
            };
            _rects.Add(band, color);
        }

        _rects.Draw(target, size);
    }

    private void CutOut(ID3D11RenderTargetView target, PixelSize size, PixelRect area, ReadOnlySpan<PixelRect> cutouts)
    {
        _rects.Clear();
        foreach (PixelRect cutout in cutouts)
        {
            _rects.Add(SurfaceGeometry.ToMonitor(cutout, area), Vector4.Zero);
        }

        _rects.Draw(target, size, _replace);
    }

    /// <summary>The desktop area the target shows. Without monitor bounds the target sits at the desktop origin.</summary>
    private static PixelRect MonitorArea(in OverlayFrame frame, PixelSize size) =>
        frame.MonitorBounds.IsEmpty ? PixelRect.FromSize(0, 0, size.Width, size.Height) : frame.MonitorBounds;

    private static Vector4 Opaque(byte red, byte green, byte blue) => new(red / 255f, green / 255f, blue / 255f, 1f);

    private void DrawTestPattern(ID3D11RenderTargetView target, PixelSize size, float scaleFactor)
    {
        var constants = new TestPatternConstants(TestPatternLayout.Create(size, scaleFactor));
        _context.UpdateSubresource(in constants, _testPatternConstants);

        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(0f, 0f, size.Width, size.Height);
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_fullscreenTriangle);
        _context.PSSetShader(_testPattern);
        _context.PSSetConstantBuffer(0, _testPatternConstants);
        _context.Draw(3, 0);
    }

    /// <summary>Mirror of the cbuffer in TestPattern.ps.hlsl. Field order and types must match.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TestPatternConstants
    {
        private readonly float _width;
        private readonly float _height;
        private readonly float _unit;
        private readonly float _gridSpacing;
        private readonly Rect _ramp;
        private readonly Rect _shade;
        private readonly float _border;
        private readonly float _corner;
        private readonly float _crossLength;
        private readonly float _padding;

        public TestPatternConstants(TestPatternLayout layout)
        {
            _width = layout.Size.Width;
            _height = layout.Size.Height;
            _unit = layout.Unit;
            _gridSpacing = layout.GridSpacing;
            _ramp = new Rect(layout.AlphaRamp);
            _shade = new Rect(layout.Shade);
            _border = layout.Border;
            _corner = layout.Corner;
            _crossLength = layout.CrossLength;
            _padding = 0f;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Rect(PixelRect rect)
        {
            private readonly float _left = rect.Left;
            private readonly float _top = rect.Top;
            private readonly float _right = rect.Right;
            private readonly float _bottom = rect.Bottom;
        }
    }
}
