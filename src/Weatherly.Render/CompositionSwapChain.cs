using Microsoft.Win32.SafeHandles;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Render;

/// <summary>
/// A swap chain with per-pixel transparency that DirectComposition can show: premultiplied alpha,
/// BGRA, flip model with two buffers, and at most one queued frame so input-to-photon latency stays low.
/// </summary>
public sealed class CompositionSwapChain : IDisposable
{
    private static readonly TimeSpan s_frameWaitTimeout = TimeSpan.FromSeconds(1);

    private readonly IDXGISwapChain1 _swapChain;
    private readonly FrameLatencyHandle _frameLatency;

    public CompositionSwapChain(ID3D11Device device, PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Height, 1);

        Size = size;

        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        var description = new SwapChainDescription1(
            (uint)size.Width,
            (uint)size.Height,
            Format.B8G8R8A8_UNorm,
            stereo: false,
            Usage.RenderTargetOutput,
            bufferCount: 2,
            Scaling.Stretch,
            SwapEffect.FlipSequential,
            AlphaMode.Premultiplied,
            SwapChainFlags.FrameLatencyWaitableObject);

        _swapChain = factory.CreateSwapChainForComposition(device, description);

        using (IDXGISwapChain2 swapChain2 = _swapChain.QueryInterface<IDXGISwapChain2>())
        {
            swapChain2.MaximumFrameLatency = 1;
            _frameLatency = new FrameLatencyHandle(swapChain2.FrameLatencyWaitableObject);
        }

        // In Direct3D 11 buffer 0 always is the current back buffer, so one view serves every frame.
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        BackBuffer = device.CreateRenderTargetView(backBuffer);
    }

    public PixelSize Size { get; }

    /// <summary>Render target of the frame that the next <see cref="Present"/> shows.</summary>
    public ID3D11RenderTargetView BackBuffer { get; }

    /// <summary>The DXGI swap chain, to hand it to a DirectComposition visual.</summary>
    public IUnknown CompositionContent => _swapChain;

    /// <summary>
    /// Blocks until the swap chain accepts a new frame. Call it before rendering, so a frame is
    /// never drawn earlier than necessary.
    /// </summary>
    public void WaitForFrame()
    {
        // A timeout is not an error: the compositor may be paused (locked session, display off).
        // Rendering then simply continues, and Present reports whether the device is still alive.
        _frameLatency.WaitOne(s_frameWaitTimeout);
    }

    /// <summary>Shows the frame in <see cref="BackBuffer"/> at the next vertical blank.</summary>
    public PresentResult Present()
    {
        Result result = _swapChain.Present(1, PresentFlags.None);
        if (DeviceLoss.IsDeviceLost(result))
        {
            return PresentResult.DeviceLost;
        }

        result.CheckError();
        return PresentResult.Presented;
    }

    public void Dispose()
    {
        BackBuffer.Dispose();
        _frameLatency.Dispose();
        _swapChain.Dispose();
    }

    /// <summary>Wraps the waitable object of the swap chain. The handle belongs to the caller and is closed on dispose.</summary>
    private sealed class FrameLatencyHandle : WaitHandle
    {
        public FrameLatencyHandle(nint handle)
        {
            SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
        }
    }
}
