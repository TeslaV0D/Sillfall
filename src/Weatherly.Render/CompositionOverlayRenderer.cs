using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Render;

/// <summary>
/// Renders overlays with Direct3D 11 and shows them through DirectComposition.
/// One device serves all monitors. The device is created on first use, on the overlay thread.
/// </summary>
public sealed class CompositionOverlayRenderer : IOverlayRenderer
{
    private readonly GraphicsDriver _driver;
    private DeviceResources? _resources;

    public CompositionOverlayRenderer(GraphicsDriver driver = GraphicsDriver.Hardware)
    {
        _driver = driver;
    }

    public string AdapterName => GraphicsDeviceFactory.GetAdapterName(Resources.Device);

    private DeviceResources Resources => _resources ??= new DeviceResources(_driver);

    public IOverlaySurface CreateSurface(nint windowHandle, PixelSize size) =>
        new Surface(Resources, windowHandle, size);

    public void RecreateDevice()
    {
        _resources?.Dispose();
        _resources = null;
    }

    public void Dispose()
    {
        _resources?.Dispose();
        _resources = null;
    }

    /// <summary>Everything that dies together with the Direct3D device.</summary>
    private sealed class DeviceResources : IDisposable
    {
        public DeviceResources(GraphicsDriver driver)
        {
            Device = GraphicsDeviceFactory.Create(driver);
            Painter = new OverlayPainter(Device);

            using IDXGIDevice dxgiDevice = Device.QueryInterface<IDXGIDevice>();
            Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        }

        public ID3D11Device Device { get; }

        public OverlayPainter Painter { get; }

        public IDCompositionDevice Composition { get; }

        public void Dispose()
        {
            Composition.Dispose();
            Painter.Dispose();
            Device.Dispose();
        }
    }

    /// <summary>Swap chain of one overlay window plus the composition visual that puts it on screen.</summary>
    private sealed class Surface : IOverlaySurface
    {
        private readonly DeviceResources _resources;
        private readonly CompositionSwapChain _swapChain;
        private readonly IDCompositionTarget _target;
        private readonly IDCompositionVisual _visual;

        public Surface(DeviceResources resources, nint windowHandle, PixelSize size)
        {
            _resources = resources;
            _swapChain = new CompositionSwapChain(resources.Device, size);

            // The visual tree of the window is just this one swap chain, in front of everything else in the window.
            resources.Composition.CreateTargetForHwnd(windowHandle, topmost: true, out _target).CheckError();
            resources.Composition.CreateVisual(out _visual).CheckError();
            _visual.SetContent(_swapChain.CompositionContent).CheckError();
            _target.SetRoot(_visual).CheckError();
            resources.Composition.Commit().CheckError();
        }

        public PixelSize Size => _swapChain.Size;

        public PresentResult Present(in OverlayFrame frame)
        {
            try
            {
                _swapChain.WaitForFrame();
                _resources.Painter.Paint(_swapChain.BackBuffer, Size, frame);
                return _swapChain.Present();
            }
            catch (SharpGenException exception) when (DeviceLoss.IsDeviceLost(exception))
            {
                return PresentResult.DeviceLost;
            }
        }

        public void Dispose()
        {
            _visual.Dispose();
            _target.Dispose();
            _swapChain.Dispose();
        }
    }
}
