using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Weatherly.Render;

/// <summary>Which Direct3D driver backs a device.</summary>
public enum GraphicsDriver
{
    /// <summary>The system GPU. Used by the overlay.</summary>
    Hardware,

    /// <summary>WARP software rasterizer. Deterministic and available without a GPU, used for headless golden images.</summary>
    Warp,
}

/// <summary>Creates Direct3D 11 devices with the flags every Weatherly pipeline relies on.</summary>
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
        DriverType driverType = driver switch
        {
            GraphicsDriver.Hardware => DriverType.Hardware,
            GraphicsDriver.Warp => DriverType.Warp,
            _ => throw new ArgumentOutOfRangeException(nameof(driver), driver, null),
        };

        D3D11.D3D11CreateDevice(
            adapter: null,
            driverType,
            DeviceCreationFlags.BgraSupport,
            s_featureLevels,
            out ID3D11Device? device).CheckError();

        return device!;
    }
}
