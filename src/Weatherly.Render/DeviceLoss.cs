using SharpGen.Runtime;

namespace Weatherly.Render;

/// <summary>Recognizes the DXGI results that mean "the graphics device is gone, rebuild everything".</summary>
internal static class DeviceLoss
{
    public static bool IsDeviceLost(Result result) =>
        result == Vortice.DXGI.ResultCode.DeviceRemoved
        || result == Vortice.DXGI.ResultCode.DeviceReset
        || result == Vortice.DXGI.ResultCode.DeviceHung;

    public static bool IsDeviceLost(SharpGenException exception) => IsDeviceLost(exception.ResultCode);
}
