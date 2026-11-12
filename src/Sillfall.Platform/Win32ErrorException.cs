using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sillfall.Platform;

/// <summary>A Win32 call failed. Carries the name of the call and the system error.</summary>
public sealed class Win32ErrorException : Win32Exception
{
    public Win32ErrorException()
    {
    }

    public Win32ErrorException(string operation)
        : this(operation, Marshal.GetLastPInvokeError())
    {
    }

    public Win32ErrorException(string operation, Exception innerException)
        : base(operation, innerException)
    {
    }

    private Win32ErrorException(string operation, int errorCode)
        : base(errorCode, $"{operation} failed: {new Win32Exception(errorCode).Message} (error {errorCode})")
    {
    }
}
