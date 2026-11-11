using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Render;

/// <summary>
/// Draws overlay content into a render target. Used for the on-screen overlays and for offscreen
/// targets (tests, golden images), so both go through the same pipeline.
/// Output is premultiplied alpha, and everything that is not drawn stays fully transparent.
/// </summary>
public sealed class OverlayPainter : IDisposable
{
    private static readonly Color4 s_transparent = new(0f, 0f, 0f, 0f);

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _fullscreenTriangle;
    private readonly ID3D11PixelShader _testPattern;
    private readonly ID3D11Buffer _testPatternConstants;
    private readonly ID3D11RasterizerState _rasterizerState;

    public OverlayPainter(ID3D11Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _context = device.ImmediateContext;
        _fullscreenTriangle = device.CreateVertexShader(ShaderLibrary.Load("FullscreenTriangle.vs"));
        _testPattern = device.CreatePixelShader(ShaderLibrary.Load("TestPattern.ps"));
        _testPatternConstants = device.CreateBuffer((uint)Marshal.SizeOf<TestPatternConstants>(), BindFlags.ConstantBuffer);
        _rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    /// <summary>Clears <paramref name="target"/> to transparent and draws the content of <paramref name="frame"/>.</summary>
    public void Paint(ID3D11RenderTargetView target, PixelSize size, in OverlayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(target);

        _context.ClearRenderTargetView(target, s_transparent);

        if (frame.Content == OverlayContent.TestPattern)
        {
            DrawTestPattern(target, size, frame.ScaleFactor);
        }

        // Leaves no reference to the target in the pipeline, flip-model swap chains require that before Present.
        _context.ClearState();
    }

    public void Dispose()
    {
        _rasterizerState.Dispose();
        _testPatternConstants.Dispose();
        _testPattern.Dispose();
        _fullscreenTriangle.Dispose();

        // The immediate context belongs to the device and is released with it.
    }

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
