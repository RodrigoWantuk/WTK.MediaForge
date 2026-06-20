using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WTK.MediaForge.Graphics.D3D11;

/// <summary>
/// Owns a Win32 shared handle used for D3D11/Vulkan interop.
/// Use <see cref="DuplicateFrom"/> to transfer ownership to another consumer.
/// </summary>
public sealed class SharedWin32Handle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SharedWin32Handle()
        : base(true)
    {
    }

    private SharedWin32Handle(nint handle)
        : base(true)
    {
        SetHandle(handle);
    }

    public static SharedWin32Handle DuplicateFrom(SharedWin32Handle source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsInvalid)
            throw new InvalidOperationException("Cannot duplicate an invalid shared handle.");

        return DuplicateFromRaw(source.DangerousGetHandleForInterop());
    }

    internal static SharedWin32Handle FromOwnedRaw(nint handle)
    {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Shared handle must be non-zero.");

        return new SharedWin32Handle(handle);
    }

    internal static SharedWin32Handle DuplicateFromRaw(nint source) =>
        FromOwnedRaw(Win32NativeHandle.DuplicateSharedHandle(source));

    /// <summary>
    /// Returns the raw handle for interop APIs. Do not close manually; use <see cref="DuplicateFrom"/> for ownership transfer.
    /// </summary>
    internal nint DangerousGetHandleForInterop() => handle;

    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
