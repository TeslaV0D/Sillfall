using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Sillfall.Render;

/// <summary>Which Direct3D driver backs a device.</summary>
public enum GraphicsDriver
{
    /// <summary>The system GPU. On machines with two GPUs the power-saving one is preferred. Used by the overlay.</summary>
    Hardware,

    /// <summary>WARP software rasterizer. Deterministic and available without a GPU, used for headless golden images.</summary>
    Warp,
}

/// <summary>Creates Direct3D 11 devices with the flags every Sillfall pipeline relies on.</summary>
public static class GraphicsDeviceFactory
{
    private static readonly FeatureLevel[] s_featureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
    ];

    /// <summary>
    /// Creates a device for <paramref name="driver"/>. BGRA support is always requested because
    /// DirectComposition swap chains and Direct2D interop both need it.
    /// </summary>
    /// <exception cref="SharpGen.Runtime.SharpGenException">Device creation failed.</exception>
    public static ID3D11Device Create(GraphicsDriver driver)
    {
        return driver switch
        {
            GraphicsDriver.Hardware => CreateHardware(),
            GraphicsDriver.Warp => Create(adapter: null, DriverType.Warp),
            _ => throw new ArgumentOutOfRangeException(nameof(driver), driver, null),
        };
    }

    /// <summary>Returns the name of the adapter that <paramref name="device"/> runs on.</summary>
    public static string GetAdapterName(ID3D11Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        return adapter.Description.Description;
    }

    /// <summary>
    /// A weather overlay must not wake up the big GPU of a hybrid laptop,
    /// so the adapter with the lowest power draw is asked for first.
    /// </summary>
    private static ID3D11Device CreateHardware()
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using IDXGIFactory6? factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();

        if (factory6 is not null
            && factory6.EnumAdapterByGpuPreference(0, GpuPreference.MinimumPower, out IDXGIAdapter1? adapter).Success
            && adapter is not null)
        {
            using (adapter)
            {
                // With an explicit adapter Direct3D requires the driver type "unknown".
                return Create(adapter, DriverType.Unknown);
            }
        }

        return Create(adapter: null, DriverType.Hardware);
    }

    private static ID3D11Device Create(IDXGIAdapter? adapter, DriverType driverType)
    {
        D3D11.D3D11CreateDevice(
            adapter,
            driverType,
            DeviceCreationFlags.BgraSupport,
            s_featureLevels,
            out ID3D11Device? device).CheckError();

        return device!;
    }
}
